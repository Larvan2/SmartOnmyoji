import { useEffect, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Progress } from "@/components/ui/progress"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { ManagePage } from "@/features/manage/ManagePage"
import { RunPage } from "@/features/run/RunPage"
import { useEngineStore } from "@/stores/engine"

const PANEL = "flex min-h-0 flex-1 flex-col"

export function App() {
  const [tab, setTab] = useState("run")
  // The manage tab loads its data on first visit, then stays mounted like the run
  // tab so an unsaved draft survives switching back and forth.
  const [manageVisited, setManageVisited] = useState(false)

  useEffect(() => useEngineStore.getState().connect(), [])

  return (
    <Tabs
      value={tab}
      onValueChange={(next: string) => {
        setTab(next)
        if (next === "manage") setManageVisited(true)
      }}
      className="h-full gap-0"
    >
      <header className="flex shrink-0 items-center justify-between gap-5 border-b bg-card px-4 py-2.5">
        <div className="flex items-center gap-5">
          <div>
            <div className="font-heading text-base leading-tight font-bold">
              SmartOnmyoji
            </div>
            <div className="text-xs text-muted-foreground">御魂助手</div>
          </div>
          <TabsList>
            <TabsTrigger value="run">运行</TabsTrigger>
            <TabsTrigger value="manage">目标管理</TabsTrigger>
          </TabsList>
        </div>
        <EngineStatus />
      </header>

      {/* Hidden by class, not left to the panel's own `hidden` attribute: Base UI
          only sets that once its exit transition has finished, and until then
          both kept-mounted panels would share the viewport. */}
      <TabsContent
        value="run"
        keepMounted
        className={tab === "run" ? PANEL : "hidden"}
      >
        <RunPage />
      </TabsContent>
      <TabsContent
        value="manage"
        keepMounted
        className={tab === "manage" ? PANEL : "hidden"}
      >
        {manageVisited && <ManagePage />}
      </TabsContent>
    </Tabs>
  )
}

function EngineStatus() {
  const connection = useEngineStore((s) => s.connection)
  const running = useEngineStore((s) => s.running)
  const paused = useEngineStore((s) => s.paused)
  const round = useEngineStore((s) => s.round)
  const progress = useEngineStore((s) => s.progress)

  // While the event stream is down the last known round and progress may be long
  // out of date, so they are withheld rather than shown as if current.
  if (connection === "closed")
    return <Badge variant="destructive">与后端的连接已断开,正在重连…</Badge>

  return (
    <div className="flex items-center gap-3.5">
      <Badge variant={!running ? "secondary" : paused ? "warning" : "success"}>
        {!running ? "空闲" : paused ? "已暂停" : "运行中"}
      </Badge>
      <span className="flex items-baseline gap-1.5">
        <span className="text-xs text-muted-foreground">回合</span>
        <b className="text-lg leading-none tabular-nums">{round}</b>
      </span>
      <Progress
        value={Math.max(0, Math.min(100, progress))}
        aria-label="运行进度"
        className="w-40"
      />
      <span className="w-9 text-right text-xs text-muted-foreground tabular-nums">
        {progress}%
      </span>
    </div>
  )
}
