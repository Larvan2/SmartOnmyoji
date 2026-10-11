import { create } from "zustand"
import { subscribeWithSelector } from "zustand/middleware"

import {
  engineEventSchema,
  type EngineEvent,
  type Point,
  type Size,
} from "@/api/schemas"

import { createReplayFilter } from "./replay"

const MAX_LOG_LINES = 800
// Matches the backend's per-subscriber channel bound: a replay can never be longer.
const REPLAY_MEMORY = 1000

export interface LogLine {
  id: number
  time: string
  kind: string
  text: string
  thumb?: string
  mark?: Point
}

export interface ClickPoint {
  x: number
  y: number
  name: string
  /** ms timestamp */
  t: number
}

interface EngineState {
  /** "closed" = the event stream dropped; round/progress are stale until it reopens. */
  connection: "connecting" | "open" | "closed"
  running: boolean
  paused: boolean
  round: number
  progress: number
  lines: LogLine[]
  /** Clicks of the current run only, in client-area pixels. */
  clicks: ClickPoint[]
  /** Client-area size of the windows chosen for the current run. */
  clientSize: Size | null

  connect: () => void
  appendLog: (kind: string, text: string) => void
  clearLog: () => void
  clearClicks: () => void
  resetRun: (clientSize: Size | null) => void
}

let nextLineId = 1
let source: EventSource | null = null

function clock(at?: string) {
  const d = at ? new Date(at) : new Date()
  return d.toLocaleTimeString("zh-CN", { hour12: false })
}

export const useEngineStore = create<EngineState>()(
  subscribeWithSelector((set) => {
    function push(
      kind: string,
      text: string,
      extra?: Partial<LogLine>,
      at?: string
    ) {
      const line: LogLine = {
        id: nextLineId++,
        time: clock(at),
        kind,
        text,
        ...extra,
      }
      set((s) => ({ lines: [...s.lines, line].slice(-MAX_LOG_LINES) }))
    }

    function apply(evt: EngineEvent) {
      switch (evt.type) {
        case "state":
          set({
            running: evt.running,
            paused: evt.paused,
            round: evt.round,
            progress: evt.progress,
          })
          break
        case "round":
          set({ round: evt.round })
          push("round", `第 ${evt.round} 轮开始`, undefined, evt.at)
          break
        case "matched":
          push(
            "matched",
            `匹配 ${evt.name} @(${evt.x},${evt.y}) 分数 ${evt.score.toFixed(2)}`,
            { thumb: evt.thumb, mark: evt.mark },
            evt.at
          )
          break
        case "clicked": {
          const pts = evt.points.map((p) => `(${p.x},${p.y})`).join(", ")
          push("clicked", `点击 ${evt.name} → ${pts}`, undefined, evt.at)
          const t = Date.now()
          set((s) => ({
            clicks: [
              ...s.clicks,
              ...evt.points.map((p) => ({ x: p.x, y: p.y, name: evt.name, t })),
            ],
          }))
          break
        }
        case "waiting":
          push(
            "waiting",
            `等待 ${evt.seconds.toFixed(1)}s（${evt.reason}）`,
            undefined,
            evt.at
          )
          break
        case "progress":
          set({ progress: evt.percent })
          break
        case "paused":
          push("paused", evt.paused ? "已暂停" : "已继续", undefined, evt.at)
          break
        case "stopped":
          push("stopped", `停止:${evt.reason}`, undefined, evt.at)
          break
        case "error":
          push("error", `错误:${evt.message}`, undefined, evt.at)
          break
        case "log":
          push((evt.level || "info").toLowerCase(), evt.text, undefined, evt.at)
          break
      }
    }

    return {
      connection: "connecting",
      running: false,
      paused: false,
      round: 0,
      progress: 0,
      lines: [],
      clicks: [],
      clientSize: null,

      connect: () => {
        if (source) return
        source = new EventSource("/api/events")

        const replay = createReplayFilter(REPLAY_MEMORY)

        source.onopen = () => {
          replay.opened()
          set({ connection: "open" })
        }
        // EventSource reconnects by itself; only the status needs tracking.
        source.onerror = () => set({ connection: "closed" })
        source.onmessage = (e: MessageEvent<string>) => {
          const raw = e.data
          const parsed = engineEventSchema.safeParse(JSON.parse(raw))
          if (!parsed.success) {
            push("info", raw)
            return
          }
          // State snapshots have no timestamp and always reflect the present.
          if (parsed.data.type !== "state" && replay.isReplay(raw)) return
          apply(parsed.data)
        }
      },

      appendLog: (kind, text) => push(kind, text),
      clearLog: () => set({ lines: [] }),
      clearClicks: () => set({ clicks: [] }),
      resetRun: (clientSize) => set({ clicks: [], clientSize }),
    }
  })
)
