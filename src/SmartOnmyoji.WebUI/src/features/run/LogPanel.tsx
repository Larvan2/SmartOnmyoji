import { memo, useLayoutEffect, useRef, useState } from "react"
import { cn } from "cn"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Label } from "@/components/ui/label"
import { AnalyzeDialog } from "@/features/analyze/AnalyzeDialog"
import { useEngineStore, type LogLine } from "@/stores/engine"

// Per log kind: [kind tag, message]. Kinds not listed use the default colours.
const KIND_STYLE: Record<string, [tag: string, message: string]> = {
  round: ["text-primary font-semibold", "text-primary font-semibold"],
  matched: ["text-info", ""],
  clicked: ["text-success", ""],
  waiting: ["text-warning", "text-warning"],
  warning: ["text-warning", "text-warning"],
  paused: ["text-warning font-semibold", "text-warning font-semibold"],
  error: ["text-destructive", "text-destructive"],
  stopped: ["text-destructive font-semibold", "text-destructive font-semibold"],
}

export function LogPanel() {
  const lines = useEngineStore((s) => s.lines)
  const [autoScroll, setAutoScroll] = useState(true)
  const [analyzeOpen, setAnalyzeOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    if (autoScroll && box.current)
      box.current.scrollTop = box.current.scrollHeight
  }, [lines, autoScroll])

  return (
    <Card className="min-h-70 min-[720px]:col-span-2 min-[1080px]:col-span-1 min-[1080px]:min-h-0">
      <CardHeader className="border-b">
        <CardTitle>运行日志</CardTitle>
        <CardAction className="flex items-center gap-3">
          <Label className="font-normal text-muted-foreground">
            <Checkbox checked={autoScroll} onCheckedChange={setAutoScroll} />
            自动滚动
          </Label>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setAnalyzeOpen(true)}
          >
            点击分析
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => useEngineStore.getState().clearLog()}
          >
            清空
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent
        ref={box}
        className="min-h-0 flex-1 overflow-y-auto font-mono text-xs leading-relaxed"
      >
        {lines.map((line) => (
          <LogRow key={line.id} line={line} />
        ))}
      </CardContent>
      <AnalyzeDialog open={analyzeOpen} onOpenChange={setAnalyzeOpen} />
    </Card>
  )
}

const LogRow = memo(function LogRow({ line }: { line: LogLine }) {
  const [tag, message] = KIND_STYLE[line.kind] ?? ["", ""]
  return (
    <div className="grid grid-cols-[62px_66px_1fr] gap-2 rounded-sm px-1 hover:bg-muted/60">
      <span className="text-muted-foreground">{line.time}</span>
      <span className={cn("text-[11px] text-muted-foreground uppercase", tag)}>
        {line.kind}
      </span>
      <span className={cn("break-words", message)}>
        {line.text}
        {line.thumb && (
          // relative wrapper so the click position can be overlaid on the thumbnail
          <span className="relative mt-1 block w-fit leading-none">
            <img
              src={line.thumb}
              alt=""
              className="max-h-30 max-w-50 rounded-md border"
            />
            {line.mark && (
              <span
                title="点击位置"
                className="pointer-events-none absolute size-2.5 -translate-1/2 rounded-full border-[1.5px] border-white bg-destructive shadow-[0_0_3px_1px] shadow-destructive/60"
                style={{
                  left: `${(line.mark.x * 100).toFixed(1)}%`,
                  top: `${(line.mark.y * 100).toFixed(1)}%`,
                }}
              />
            )}
          </span>
        )}
      </span>
    </div>
  )
})
