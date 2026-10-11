import { useEffect, useState } from "react"

import { errorMessage } from "@/api/client"
import { pickWindow, useWindows } from "@/api/queries"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { notify } from "@/lib/notify"

const PICK_SECONDS = 3

export function WindowList({
  selected,
  onChange,
  disabled,
}: {
  selected: ReadonlySet<string>
  onChange: (next: ReadonlySet<string>) => void
  disabled: boolean
}) {
  const windows = useWindows()
  const [filter, setFilter] = useState("阴阳师")
  const [picking, setPicking] = useState(false)

  // A window that was closed and reopened gets a new handle. A stale handle left in
  // the selection would make every start fail with "window not found", while its
  // checkbox is already gone from the list and cannot be unticked.
  const all = windows.data
  useEffect(() => {
    if (!all) return
    const alive = new Set(all.map((w) => w.handle))
    const kept = [...selected].filter((h) => alive.has(h))
    if (kept.length === selected.size) return
    onChange(new Set(kept))
    notify.info(
      `已取消 ${selected.size - kept.length} 个已关闭窗口的选择,请重新勾选`
    )
  }, [all, selected, onChange])

  const needle = filter.trim().toLowerCase()
  const shown = (all ?? []).filter(
    (w) => !needle || w.title.toLowerCase().includes(needle)
  )

  const refresh = async () => {
    const r = await windows.refetch()
    if (r.error) notify.error(errorMessage(r.error))
  }

  const pick = async () => {
    notify.info(`${PICK_SECONDS} 秒内切到游戏窗口…`)
    setPicking(true)
    try {
      const w = await pickWindow(PICK_SECONDS)
      if (!w) {
        notify.error("未取到前台窗口")
        return
      }
      // The list only holds visible, titled windows — the same rule the backend
      // applies at start — so a picked window missing from it cannot be run.
      const listed = (await windows.refetch()).data ?? []
      if (!listed.some((x) => x.handle === w.handle)) {
        notify.error(`「${w.title || "(无标题)"}」不在可选窗口列表里`)
        return
      }
      onChange(new Set(selected).add(w.handle))
      notify.ok(`已选:${w.title}`)
    } catch (e) {
      notify.error("点选失败:" + errorMessage(e))
    } finally {
      setPicking(false)
    }
  }

  const toggle = (handle: string, checked: boolean) => {
    const next = new Set(selected)
    if (checked) next.add(handle)
    else next.delete(handle)
    onChange(next)
  }

  return (
    <Card className="min-h-0 flex-1">
      <CardHeader>
        <CardTitle>游戏窗口</CardTitle>
      </CardHeader>
      <CardContent className="flex min-h-0 flex-1 flex-col gap-2.5">
        <div className="flex items-center gap-2">
          <Input
            className="min-w-0 flex-1"
            placeholder="筛选标题…"
            aria-label="筛选窗口标题"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
          />
          <Button type="button" variant="outline" onClick={refresh}>
            刷新
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={picking}
            onClick={pick}
          >
            点选
          </Button>
        </div>

        <div className="flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto">
          {shown.map((w) => (
            <label
              key={w.handle}
              className="flex cursor-pointer items-center gap-2.5 rounded-md border px-2.5 py-1.5 hover:border-primary has-disabled:cursor-not-allowed has-disabled:hover:border-border"
            >
              <Checkbox
                checked={selected.has(w.handle)}
                onCheckedChange={(c) => toggle(w.handle, c)}
                disabled={disabled}
              />
              <span className="flex min-w-0 flex-col">
                <span className="truncate text-sm font-medium">
                  {w.title || "(无标题)"}
                </span>
                <span className="text-xs text-muted-foreground">
                  {w.handle} · pid {w.processId} · {w.width}×{w.height}
                </span>
              </span>
            </label>
          ))}
          {all && shown.length === 0 && (
            <p className="p-2 text-xs text-muted-foreground">
              {all.length ? "无匹配窗口(改筛选词或清空)" : "未枚举到窗口"}
            </p>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
