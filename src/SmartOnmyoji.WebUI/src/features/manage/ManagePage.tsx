import { useCallback, useState, type FormEvent } from "react"
import { FolderOpenIcon, RefreshCwIcon } from "lucide-react"

import { errorMessage } from "@/api/client"
import {
  createTargetSet,
  deleteTargetImage,
  invalidateTargets,
  openTargetFolder,
  saveTargetJson,
  useImageVersion,
  useTargetEdit,
  useTargetSets,
} from "@/api/queries"
import type { TargetFlag, TargetImageJson, TargetSetJson } from "@/api/schemas"
import { useConfirm } from "@/components/confirm"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyTitle,
} from "@/components/ui/empty"
import { Input } from "@/components/ui/input"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { notify } from "@/lib/notify"

import {
  CaptureDialog,
  type Capture,
  type SavedTemplate,
} from "./CaptureDialog"
import { ImageList } from "./ImageList"

/** Priority is the list position; renumber after every change to the list. */
function renumber(images: TargetImageJson[]): TargetImageJson[] {
  return images.map((image, i) => ({ ...image, priority: i + 1 }))
}

export function ManagePage() {
  const sets = useTargetSets()
  const [name, setName] = useState("")
  const edit = useTargetEdit(name)
  const [newName, setNewName] = useState("")
  const [capture, setCapture] = useState<Capture | null>(null)
  const [editorOpen, setEditorOpen] = useState(false)
  // File whose click points the editor is placing; null = the editor crops templates.
  const [pointFile, setPointFile] = useState<string | null>(null)
  const [confirm, confirmDialog] = useConfirm()
  const bumpImageVersion = useImageVersion((s) => s.bump)

  // The draft is the edit model plus unsaved changes. It restarts from the server
  // copy whenever a different model arrives (another set was selected).
  const [draft, setDraft] = useState<TargetSetJson | null>(null)
  const [source, setSource] = useState<TargetSetJson | undefined>(undefined)
  if (edit.data !== source) {
    setSource(edit.data)
    setDraft(
      edit.data ? { ...edit.data, images: renumber(edit.data.images) } : null
    )
    setPointFile(null)
  }

  const updateImages = useCallback(
    (change: (images: TargetImageJson[]) => TargetImageJson[]) =>
      setDraft((d) => d && { ...d, images: renumber(change(d.images)) }),
    []
  )

  const updateImage = (
    file: string,
    change: (image: TargetImageJson) => TargetImageJson
  ) =>
    updateImages((images) =>
      images.map((i) => (i.file === file ? change(i) : i))
    )

  const move = useCallback(
    (from: number, to: number) =>
      updateImages((images) => {
        if (from === to) return images
        const next = [...images]
        next.splice(to, 0, ...next.splice(from, 1))
        return next
      }),
    [updateImages]
  )

  const create = async (e: FormEvent) => {
    e.preventDefault()
    const created = newName.trim()
    if (!created) {
      notify.error("请输入玩法名")
      return
    }
    try {
      await createTargetSet(created)
      notify.ok("已创建目标集 " + created)
      setNewName("")
      await invalidateTargets()
      setName(created)
    } catch (err) {
      notify.error("创建失败:" + errorMessage(err))
    }
  }

  const save = async () => {
    if (!draft) return
    try {
      await saveTargetJson(name, draft)
      notify.ok("已保存 target.json")
      await invalidateTargets()
    } catch (err) {
      notify.error(errorMessage(err))
    }
  }

  const remove = async (file: string) => {
    const ok = await confirm({
      title: `删除模板 ${file}?`,
      description: "图片文件会从磁盘删除。",
      actionLabel: "删除",
      destructive: true,
    })
    if (!ok) return
    try {
      await deleteTargetImage(name, file)
      updateImages((images) => images.filter((i) => i.file !== file))
      setPointFile(null)
      notify.ok("已删除 " + file)
      await invalidateTargets()
    } catch (err) {
      notify.error("删除失败:" + errorMessage(err))
    }
  }

  const onSaved = ({ file, baseSize, replaced }: SavedTemplate) => {
    bumpImageVersion()
    updateImages((images) => {
      // Overwriting win.jpg with win.png removed the old file on disk; drop it here too.
      const kept =
        replaced && replaced.toLowerCase() !== file.toLowerCase()
          ? images.filter(
              (i) => i.file.toLowerCase() !== replaced.toLowerCase()
            )
          : images
      return kept.some((i) => i.file === file)
        ? // re-captured, possibly at another resolution
          kept.map((i) =>
            i.file === file ? { ...i, baseSize: baseSize ?? i.baseSize } : i
          )
        : [
            ...kept,
            { file, priority: 0, flag: "Normal" as TargetFlag, baseSize },
          ]
    })
    void invalidateTargets()
  }

  const editPoints = (file: string) => {
    setPointFile(file)
    setEditorOpen(true)
    notify.info(
      capture
        ? "点选模式:在截图上单击添加偏移点击点"
        : "点选模式:先在上方选窗口点「截图」,再在截图上单击"
    )
  }

  const replaceCapture = (next: Capture) => {
    if (capture) URL.revokeObjectURL(capture.url)
    setCapture(next)
  }

  const pointImage = draft?.images.find((i) => i.file === pointFile) ?? null

  return (
    <div className="grid min-h-0 flex-1 grid-cols-1 gap-3.5 overflow-y-auto p-3.5 min-[860px]:grid-cols-[400px_1fr] min-[860px]:overflow-hidden">
      <div className="flex min-h-0 flex-col gap-3.5 min-[860px]:overflow-y-auto">
        <Card>
          <CardHeader>
            <CardTitle>新建目标集</CardTitle>
            <CardDescription>
              创建后在 img/&lt;名称&gt;/ 下生成目录与空 target.json,由软件托管。
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form className="flex items-center gap-2" onSubmit={create}>
              <Input
                className="min-w-0 flex-1"
                placeholder="玩法名(拼音,如 huntu)"
                aria-label="玩法名"
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
              />
              <Button type="submit">创建</Button>
            </form>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>编辑目标集</CardTitle>
          </CardHeader>
          <CardContent className="flex items-center gap-2">
            <NativeSelect
              className="min-w-0 flex-1"
              aria-label="目标集"
              value={name}
              onChange={(e) => setName(e.target.value)}
            >
              <NativeSelectOption value="">（选择目标集）</NativeSelectOption>
              {sets.data?.map((s) => (
                <NativeSelectOption key={s.name} value={s.name}>
                  {`${s.name}（${s.imageCount} 图）`}
                </NativeSelectOption>
              ))}
            </NativeSelect>
            <Button
              variant="outline"
              size="icon"
              title="刷新"
              onClick={() => void sets.refetch()}
            >
              <RefreshCwIcon />
            </Button>
            <Button
              variant="outline"
              disabled={!name}
              title="在资源管理器中打开该目标集的文件夹"
              onClick={() =>
                openTargetFolder(name).catch((err) =>
                  notify.error(errorMessage(err))
                )
              }
            >
              <FolderOpenIcon data-icon="inline-start" />
              打开文件夹
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>截图取模板</CardTitle>
            <CardDescription>
              选窗口 → 截图 →
              框选,全在编辑器里完成;也可在右侧图片行点「偏移点」进入点选模式。
            </CardDescription>
          </CardHeader>
          <CardContent>
            {/* Without a set there is nowhere to store a template. */}
            <Button
              className="w-full"
              disabled={!draft}
              onClick={() => {
                setPointFile(null)
                setEditorOpen(true)
              }}
            >
              打开截图编辑器
            </Button>
          </CardContent>
        </Card>
      </div>

      <Card className="min-h-80 min-[860px]:min-h-0">
        <CardHeader className="border-b">
          <CardTitle>
            {draft
              ? `图片(${draft.images.length})· ${name}`
              : "图片(target.json)"}
          </CardTitle>
          <CardAction>
            <Button disabled={!draft} onClick={save}>
              保存 target.json
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent className="flex min-h-0 flex-1 flex-col">
          {draft ? (
            <ImageList
              setName={name}
              images={draft.images}
              pointFile={pointFile}
              onMove={move}
              onFlag={(file, flag) =>
                updateImage(file, (i) => ({ ...i, flag }))
              }
              onEditPoints={editPoints}
              onClearPoints={(file) =>
                updateImage(file, (i) => ({
                  ...i,
                  click: { ...i.click, clickPos: [] },
                }))
              }
              onDelete={remove}
            />
          ) : (
            <Empty>
              <EmptyHeader>
                <EmptyTitle>未选择目标集</EmptyTitle>
                <EmptyDescription>先在左侧选择要编辑的目标集</EmptyDescription>
              </EmptyHeader>
            </Empty>
          )}
        </CardContent>
      </Card>

      <CaptureDialog
        open={editorOpen}
        onOpenChange={setEditorOpen}
        setName={name}
        capture={capture}
        onCapture={replaceCapture}
        pointImage={pointImage}
        onCropMode={() => setPointFile(null)}
        onAddPoint={(point) => {
          if (pointFile)
            updateImage(pointFile, (i) => ({
              ...i,
              click: {
                ...i.click,
                clickPos: [...(i.click?.clickPos ?? []), point],
              },
            }))
        }}
        onSaved={onSaved}
        confirm={confirm}
      />
      {confirmDialog}
    </div>
  )
}
