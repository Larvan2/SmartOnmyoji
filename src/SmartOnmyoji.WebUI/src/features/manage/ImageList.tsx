import { useEffect, useRef, useState } from "react"
import { combine } from "@atlaskit/pragmatic-drag-and-drop/combine"
import {
  draggable,
  dropTargetForElements,
  monitorForElements,
} from "@atlaskit/pragmatic-drag-and-drop/element/adapter"
import { cn } from "cn"
import { GripVerticalIcon, Trash2Icon } from "lucide-react"

import { imageUrl, useImageVersion } from "@/api/queries"
import {
  flagSchema,
  type TargetFlag,
  type TargetImageJson,
} from "@/api/schemas"
import { Button } from "@/components/ui/button"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"

const FLAG_LABEL: Record<TargetFlag, string> = {
  Normal: "普通",
  RoundStart: "开局",
  Once: "单次",
  Skip: "跳过",
  Stop: "终止",
}

const ROW = "target-image-row"
const COLUMNS =
  "grid grid-cols-[20px_56px_1fr_92px_44px_auto_auto_auto] items-center gap-2"

interface Props {
  setName: string
  images: TargetImageJson[]
  /** File whose click points are being edited in the capture editor, if any. */
  pointFile: string | null
  onMove: (from: number, to: number) => void
  onFlag: (file: string, flag: TargetFlag) => void
  onEditPoints: (file: string) => void
  onClearPoints: (file: string) => void
  onDelete: (file: string) => void
}

/**
 * Template list. Match priority is simply the position in this list (top first),
 * so reordering by drag is how priority is edited.
 */
export function ImageList({ images, onMove, ...row }: Props) {
  useEffect(
    () =>
      monitorForElements({
        canMonitor: ({ source }) => source.data.type === ROW,
        onDrop: ({ source, location }) => {
          const target = location.current.dropTargets[0]
          if (target)
            onMove(source.data.index as number, target.data.index as number)
        },
      }),
    [onMove]
  )

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-1.5">
      <div className={cn(COLUMNS, "px-2 text-xs text-muted-foreground")}>
        <span />
        <span />
        <span>图片名</span>
        <span>标记</span>
        <span className="text-center">优先级</span>
      </div>
      <div className="flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto">
        {images.map((image, index) => (
          <ImageRow key={image.file} image={image} index={index} {...row} />
        ))}
      </div>
    </div>
  )
}

function ImageRow({
  setName,
  image,
  index,
  pointFile,
  onFlag,
  onEditPoints,
  onClearPoints,
  onDelete,
}: Omit<Props, "images" | "onMove"> & {
  image: TargetImageJson
  index: number
}) {
  const version = useImageVersion((s) => s.version)
  const rowRef = useRef<HTMLDivElement>(null)
  const handleRef = useRef<HTMLSpanElement>(null)
  const [dragging, setDragging] = useState(false)
  const [over, setOver] = useState(false)

  useEffect(() => {
    const element = rowRef.current!
    return combine(
      draggable({
        element,
        // only the grip starts a drag, so text in the row stays selectable
        dragHandle: handleRef.current!,
        getInitialData: () => ({ type: ROW, index }),
        onDragStart: () => setDragging(true),
        onDrop: () => setDragging(false),
      }),
      dropTargetForElements({
        element,
        canDrop: ({ source }) => source.data.type === ROW,
        getData: () => ({ index }),
        onDragEnter: () => setOver(true),
        onDragLeave: () => setOver(false),
        onDrop: () => setOver(false),
      })
    )
  }, [index])

  const points = image.click?.clickPos.length ?? 0

  return (
    <div
      ref={rowRef}
      className={cn(
        COLUMNS,
        "rounded-md border px-2 py-1.5",
        pointFile === image.file && "border-primary bg-primary/10",
        over && !dragging && "border-primary",
        dragging && "opacity-40"
      )}
    >
      <span
        ref={handleRef}
        title="拖动调整优先级顺序"
        className="flex cursor-grab justify-center text-muted-foreground"
      >
        <GripVerticalIcon className="size-4" />
      </span>
      <img
        loading="lazy"
        draggable={false}
        src={imageUrl(setName, image.file, version)}
        alt=""
        className="h-10 w-14 rounded-sm bg-muted object-cover"
      />
      <div className="min-w-0 text-xs break-all">
        {image.file}
        {/* Client-area size the template was captured at; at run time the template
            is rescaled to the current size. Templates without it are never rescaled. */}
        <span
          className="block text-[10.5px] text-muted-foreground"
          title={
            image.baseSize
              ? "换分辨率运行时,模板会按当前客户区尺寸等比缩放后匹配"
              : "这张模板没有记录截取时的分辨率(旧模板),只能在原分辨率下匹配;重新截取即可获得跨分辨率能力"
          }
        >
          {image.baseSize
            ? `基准 ${image.baseSize.width}×${image.baseSize.height}`
            : "基准未记录 · 不缩放"}
        </span>
      </div>
      <NativeSelect
        size="sm"
        className="w-full"
        aria-label="标记"
        value={image.flag}
        onChange={(e) => onFlag(image.file, flagSchema.parse(e.target.value))}
      >
        {flagSchema.options.map((f) => (
          <NativeSelectOption key={f} value={f}>
            {FLAG_LABEL[f]}
          </NativeSelectOption>
        ))}
      </NativeSelect>
      <span className="text-center text-xs text-muted-foreground tabular-nums">
        {image.priority}
      </span>
      <Button
        variant="outline"
        size="sm"
        title="在截图上点选客户区偏移点击点"
        onClick={() => onEditPoints(image.file)}
      >
        偏移点({points})
      </Button>
      <Button
        variant="outline"
        size="sm"
        disabled={points === 0}
        onClick={() => onClearPoints(image.file)}
      >
        清点
      </Button>
      <Button
        variant="destructive"
        size="icon-sm"
        title="删除模板"
        onClick={() => onDelete(image.file)}
      >
        <Trash2Icon />
      </Button>
    </div>
  )
}
