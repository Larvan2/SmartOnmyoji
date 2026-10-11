import { useEffect, useRef, useState, type PointerEvent } from "react"
import { cn } from "cn"
import { RefreshCwIcon } from "lucide-react"

import { errorMessage } from "@/api/client"
import { captureWindow, saveTargetImage, useWindows } from "@/api/queries"
import type { Size, TargetImageJson } from "@/api/schemas"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { notify } from "@/lib/notify"

import {
  clamp,
  dragRect,
  toNaturalRect,
  toNormalizedPoint,
  type Rect,
} from "./geometry"

/** A screenshot of a window's client area; width/height are its physical pixels. */
export interface Capture extends Size {
  url: string
}

export interface SavedTemplate {
  file: string
  baseSize: Size | null
  /** File that the save overwrote, when the user confirmed a name conflict. */
  replaced?: string
}

interface Props {
  open: boolean
  onOpenChange: (open: boolean) => void
  setName: string
  capture: Capture | null
  onCapture: (capture: Capture) => void
  /** Image whose click points are being picked; null = template-cropping mode. */
  pointImage: TargetImageJson | null
  onCropMode: () => void
  onAddPoint: (point: [number, number]) => void
  onSaved: (saved: SavedTemplate) => void
  confirm: (o: {
    title: string
    description?: string
    actionLabel: string
  }) => Promise<boolean>
}

/**
 * Large editor for working on a screenshot: drag to cut a template out of it, or
 * click to place offset click points for one image. Choosing the window and taking
 * the screenshot happen in here too, so the whole flow stays in one place.
 */
export function CaptureDialog({
  open,
  onOpenChange,
  setName,
  capture,
  onCapture,
  pointImage,
  onCropMode,
  onAddPoint,
  onSaved,
  confirm,
}: Props) {
  const windows = useWindows()
  const [filter, setFilter] = useState("阴阳师")
  const [pickedHandle, setPickedHandle] = useState("")
  const [fileName, setFileName] = useState("")
  const [busy, setBusy] = useState(false)
  // `drag` is in displayed pixels while the pointer is down; `selection` is the
  // finished rectangle in the screenshot's natural pixels.
  const [drag, setDrag] = useState<{
    start: { x: number; y: number }
    rect: Rect
  } | null>(null)
  const [selection, setSelection] = useState<Rect | null>(null)
  const imgRef = useRef<HTMLImageElement>(null)

  // Handles change as windows are closed and reopened; re-enumerate on every open
  // so a screenshot is never requested for a window that is gone.
  const { refetch } = windows
  useEffect(() => {
    if (open) void refetch()
  }, [open, refetch])

  const needle = filter.trim().toLowerCase()
  const shown = (windows.data ?? []).filter(
    (w) => !needle || w.title.toLowerCase().includes(needle)
  )
  const handle =
    shown.find((w) => w.handle === pickedHandle)?.handle ??
    shown[0]?.handle ??
    ""

  const cropping = pointImage === null
  const visibleSelection = cropping ? selection : null

  const takeCapture = async () => {
    if (!handle) {
      notify.error("请选择窗口")
      return
    }
    setBusy(true)
    try {
      const blob = await captureWindow(handle)
      const bitmap = await createImageBitmap(blob)
      const next = {
        url: URL.createObjectURL(blob),
        width: bitmap.width,
        height: bitmap.height,
      }
      bitmap.close()
      setSelection(null)
      onCapture(next)
    } catch (e) {
      notify.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  /** Pointer position relative to the displayed screenshot, clamped to it. */
  const locate = (e: { clientX: number; clientY: number }) => {
    const r = imgRef.current!.getBoundingClientRect()
    return {
      point: {
        x: clamp(e.clientX - r.left, 0, r.width),
        y: clamp(e.clientY - r.top, 0, r.height),
      },
      size: { width: r.width, height: r.height },
    }
  }

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (!capture || !cropping || e.button !== 0) return
    e.currentTarget.setPointerCapture(e.pointerId)
    const { point } = locate(e)
    setSelection(null)
    setDrag({ start: point, rect: { ...point, w: 0, h: 0 } })
    e.preventDefault()
  }

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag) return
    setDrag({ start: drag.start, rect: dragRect(drag.start, locate(e).point) })
  }

  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag || !capture) return
    setSelection(toNaturalRect(drag.rect, locate(e).size, capture))
    setDrag(null)
  }

  const onClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!capture || cropping) return
    const { point, size } = locate(e)
    onAddPoint(toNormalizedPoint(point, size))
  }

  const save = async () => {
    if (!selection || !capture) {
      notify.error("请先框选选区")
      return
    }
    let file = fileName.trim()
    if (!file) {
      notify.error("请输入模板文件名")
      return
    }
    if (!/\.(png|jpe?g)$/i.test(file)) file += ".png"

    const canvas = document.createElement("canvas")
    canvas.width = selection.w
    canvas.height = selection.h
    canvas
      .getContext("2d")!
      .drawImage(
        imgRef.current!,
        selection.x,
        selection.y,
        selection.w,
        selection.h,
        0,
        0,
        selection.w,
        selection.h
      )
    // The screenshot's size (= client area in physical pixels) is stored with the
    // template; it is what lets the template be rescaled for other resolutions.
    const payload = {
      file,
      dataUrl: canvas.toDataURL("image/png"),
      baseWidth: capture.width,
      baseHeight: capture.height,
    }

    setBusy(true)
    try {
      let result = await saveTargetImage(setName, payload)
      let replaced: string | undefined
      // Conflicts are compared without extension, so saving win.png while win.jpg
      // exists also asks: keeping both would match the same target twice.
      if ("conflict" in result) {
        replaced = result.conflict
        const ok = await confirm({
          title: `模板「${replaced}」已存在`,
          description: "覆盖后原模板无法恢复。",
          actionLabel: "覆盖",
        })
        if (!ok) {
          notify.info("已取消保存")
          return
        }
        result = await saveTargetImage(setName, { ...payload, overwrite: true })
        if ("conflict" in result) {
          notify.error("覆盖失败,请重试")
          return
        }
      }
      notify.ok("已保存模板 " + result.file)
      setFileName("")
      setSelection(null)
      onSaved({
        file: result.file,
        baseSize: result.baseSize ?? null,
        replaced,
      })
    } catch (e) {
      notify.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const overlay = drag
    ? {
        left: drag.rect.x,
        top: drag.rect.y,
        width: drag.rect.w,
        height: drag.rect.h,
      }
    : visibleSelection && capture
      ? {
          left: `${(visibleSelection.x / capture.width) * 100}%`,
          top: `${(visibleSelection.y / capture.height) * 100}%`,
          width: `${(visibleSelection.w / capture.width) * 100}%`,
          height: `${(visibleSelection.h / capture.height) * 100}%`,
        }
      : null

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[94vh] overflow-y-auto sm:max-w-[min(1400px,94vw)]">
        <DialogHeader>
          <DialogTitle>
            {pointImage ? `点选偏移点 · ${pointImage.file}` : "框选模板"}
          </DialogTitle>
          <DialogDescription>
            {pointImage
              ? capture
                ? "在截图上单击添加偏移点击点。"
                : "先选窗口并截图,再在截图上单击添加偏移点击点。"
              : visibleSelection
                ? `选区 ${visibleSelection.w}×${visibleSelection.h} @(${visibleSelection.x},${visibleSelection.y})`
                : "选窗口并截图,然后拖动框选要作为模板的区域。"}
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center gap-2">
          <Input
            className="w-48 shrink"
            placeholder="筛选标题…"
            aria-label="筛选窗口标题"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <NativeSelect
            className="min-w-0 flex-1"
            aria-label="窗口"
            value={handle}
            onChange={(e) => setPickedHandle(e.target.value)}
          >
            {shown.map((w) => (
              <NativeSelectOption key={w.handle} value={w.handle}>
                {`${w.title || "(无标题)"} · ${w.width}×${w.height}`}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <Button
            variant="outline"
            size="icon"
            title="刷新窗口列表"
            onClick={() => void refetch()}
          >
            <RefreshCwIcon />
          </Button>
          <Button disabled={busy} onClick={takeCapture}>
            截图
          </Button>
          {pointImage && (
            <Button variant="outline" onClick={onCropMode}>
              切回框选模板
            </Button>
          )}
        </div>

        <div
          className={cn(
            "flex touch-none items-center justify-center overflow-hidden rounded-lg border bg-muted select-none",
            !capture && "min-h-[50vh]"
          )}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onClick={onClick}
        >
          {capture ? (
            <div className="relative leading-none">
              <img
                ref={imgRef}
                src={capture.url}
                alt="窗口截图"
                draggable={false}
                className="block max-h-[70vh] max-w-full cursor-crosshair"
              />
              {overlay && (
                <div
                  className="pointer-events-none absolute border-2 border-primary bg-primary/20"
                  style={overlay}
                />
              )}
              {pointImage?.click?.clickPos.map(([x, y], i) => (
                <span
                  key={i}
                  className="pointer-events-none absolute size-3 -translate-1/2 rounded-full border-2 border-white bg-destructive shadow-sm"
                  style={{ left: `${x * 100}%`, top: `${y * 100}%` }}
                />
              ))}
            </div>
          ) : (
            <span className="text-sm text-muted-foreground">
              选窗口后点「截图」
            </span>
          )}
        </div>

        {cropping && (
          <div className="flex items-center gap-2">
            <Input
              className="min-w-0 flex-1"
              placeholder="模板文件名(如 tiaozhan)"
              aria-label="模板文件名"
              value={fileName}
              onChange={(e) => setFileName(e.target.value)}
            />
            <Button disabled={!selection || busy} onClick={save}>
              保存选区为模板
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
