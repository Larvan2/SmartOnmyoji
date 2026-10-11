import { useEffect } from "react"
import { cn } from "cn"
import { RefreshCwIcon } from "lucide-react"

import { errorMessage } from "@/api/client"
import {
  imageUrl,
  useImageVersion,
  useTargetDetail,
  useTargetSets,
} from "@/api/queries"
import type { TargetFlag, TargetSetSummary } from "@/api/schemas"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { notify } from "@/lib/notify"
import { useEngineStore } from "@/stores/engine"

const FLAG_LABEL: Record<TargetFlag, string> = {
  Normal: "",
  RoundStart: "开局",
  Once: "单次",
  Skip: "跳过",
  Stop: "终止",
}

const FLAG_EDGE: Record<TargetFlag, string> = {
  Normal: "border-t-border",
  RoundStart: "border-t-success",
  Once: "border-t-primary",
  Skip: "border-t-muted-foreground",
  Stop: "border-t-destructive",
}

/**
 * The set that will actually run: the user's pick while it is still runnable,
 * otherwise the first set that has templates. Sets without templates are listed
 * but cannot be chosen, since starting with one always fails.
 */
export function resolveTargetSet(
  sets: TargetSetSummary[] | undefined,
  picked: string
): string {
  const runnable = (sets ?? []).filter((s) => s.imageCount > 0)
  return (
    runnable.find((s) => s.name === picked)?.name ?? runnable[0]?.name ?? ""
  )
}

export function TargetSetCard({
  value,
  onChange,
}: {
  value: string
  onChange: (name: string) => void
}) {
  const sets = useTargetSets()
  const detail = useTargetDetail(value)
  const version = useImageVersion((s) => s.version)

  const warnings = detail.data?.warnings
  useEffect(() => {
    for (const w of warnings ?? [])
      useEngineStore.getState().appendLog("warning", "[目标集] " + w)
  }, [warnings])

  const refresh = async () => {
    const r = await sets.refetch()
    if (r.error) notify.error(errorMessage(r.error))
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>目标集(玩法)</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2.5">
        <div className="flex items-center gap-2">
          <NativeSelect
            className="min-w-0 flex-1"
            aria-label="目标集"
            value={value}
            onChange={(e) => onChange(e.target.value)}
          >
            {sets.data?.map((s) => (
              <NativeSelectOption
                key={s.name}
                value={s.name}
                disabled={s.imageCount === 0}
              >
                {s.imageCount === 0
                  ? `${s.name}（尚无模板）`
                  : `${s.name}（${s.imageCount} 图 · ${s.source}）`}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <Button
            type="button"
            variant="outline"
            size="icon"
            title="刷新目标集"
            onClick={refresh}
          >
            <RefreshCwIcon />
          </Button>
        </div>

        {detail.data && (
          <>
            <p className="text-xs text-muted-foreground">
              来源 {detail.data.source} · {detail.data.images.length} 图
              {detail.data.warnings.length > 0 &&
                ` · ${detail.data.warnings.length} 条告警`}
            </p>
            <div className="grid max-h-64 grid-cols-[repeat(auto-fill,minmax(72px,1fr))] gap-2 overflow-y-auto">
              {detail.data.images.map((img) => (
                <figure
                  key={img.file}
                  title={`${img.file}\nflag=${img.flag} 优先级=${img.priority}${img.hasClick ? " 偏移点击" : ""}`}
                  className={cn(
                    "flex flex-col overflow-hidden rounded-md border border-t-[3px] bg-background",
                    FLAG_EDGE[img.flag]
                  )}
                >
                  <img
                    loading="lazy"
                    src={imageUrl(detail.data.name, img.file, version)}
                    alt=""
                    className="h-13 w-full bg-muted object-cover"
                  />
                  <figcaption className="truncate px-1 py-0.5 text-[10px] text-muted-foreground">
                    {img.flag === "Normal"
                      ? img.name
                      : `${img.name} · ${FLAG_LABEL[img.flag]}`}
                  </figcaption>
                </figure>
              ))}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}
