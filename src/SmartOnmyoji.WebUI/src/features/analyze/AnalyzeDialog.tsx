import { useEffect, useRef } from "react"
import { cn } from "cn"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { useEngineStore } from "@/stores/engine"

import { drawScatter, drawTargetBars, drawTimeBars } from "./charts"

/** Click-position statistics for the current run. Data lives in the event stream only. */
export function AnalyzeDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[92vh] overflow-y-auto sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>点击位置分析 · 本次运行</DialogTitle>
          <DialogDescription
            render={<div />}
            className="flex items-center gap-3"
          >
            <Summary />
            <Button
              variant="outline"
              size="sm"
              onClick={() => useEngineStore.getState().clearClicks()}
            >
              清空
            </Button>
          </DialogDescription>
        </DialogHeader>
        <Charts />
      </DialogContent>
    </Dialog>
  )
}

function Summary() {
  const clicks = useEngineStore((s) => s.clicks)
  if (!clicks.length) return <span>本次运行暂无点击</span>
  const names = new Set(clicks.map((c) => c.name)).size
  return (
    <span>
      共 {clicks.length} 次点击 · {names} 个目标
    </span>
  )
}

// Mounted only while the dialog is open, so nothing is drawn when it is closed.
// The canvases are redrawn straight from the store, bypassing React rendering.
function Charts() {
  const scatter = useRef<HTMLCanvasElement>(null)
  const targets = useRef<HTMLCanvasElement>(null)
  const time = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    let frame = 0
    const draw = () => {
      frame = 0
      const { clicks, clientSize } = useEngineStore.getState()
      if (scatter.current) drawScatter(scatter.current, clicks, clientSize)
      if (targets.current) drawTargetBars(targets.current, clicks)
      if (time.current) drawTimeBars(time.current, clicks)
    }
    // at most one redraw per frame, however fast clicks arrive
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(draw)
    }

    draw()
    const unsubscribe = useEngineStore.subscribe((s) => s.clicks, schedule)
    window.addEventListener("resize", schedule)
    return () => {
      unsubscribe()
      window.removeEventListener("resize", schedule)
      cancelAnimationFrame(frame)
    }
  }, [])

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Chart title="点击散点(客户区坐标,按目标着色)" canvasRef={scatter} wide />
      <Chart title="各目标点击次数" canvasRef={targets} />
      <Chart title="点击时段分布" canvasRef={time} />
    </div>
  )
}

function Chart({
  title,
  canvasRef,
  wide,
}: {
  title: string
  canvasRef: React.RefObject<HTMLCanvasElement | null>
  wide?: boolean
}) {
  return (
    <figure
      className={cn("flex min-w-0 flex-col gap-1.5", wide && "sm:col-span-2")}
    >
      <figcaption className="text-xs font-medium text-muted-foreground">
        {title}
      </figcaption>
      <canvas
        ref={canvasRef}
        className="h-56 w-full rounded-lg border bg-muted/40"
      />
    </figure>
  )
}
