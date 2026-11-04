import { useEffect, useState } from "react"
import { zodResolver } from "@hookform/resolvers/zod"
import {
  LockIcon,
  PauseIcon,
  PlayIcon,
  SquareIcon,
  TriangleAlertIcon,
} from "lucide-react"
import { useForm } from "react-hook-form"

import { errorMessage } from "@/api/client"
import {
  pauseEngine,
  resumeEngine,
  saveOptions,
  startEngine,
  stopEngine,
  useOptions,
  useTargetSets,
  useWindows,
} from "@/api/queries"
import { optionsFormSchema, optionsSchema, type Options } from "@/api/schemas"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Spinner } from "@/components/ui/spinner"
import { notify } from "@/lib/notify"
import { useEngineStore } from "@/stores/engine"

import { LogPanel } from "./LogPanel"
import { OptionsForm } from "./OptionsForm"
import { resolveTargetSet, TargetSetCard } from "./TargetSetCard"
import { WindowList } from "./WindowList"

const SAVE_DEBOUNCE_MS = 600

export function RunPage() {
  const options = useOptions()
  if (options.isPending)
    return (
      <div className="grid flex-1 place-items-center">
        <Spinner />
      </div>
    )
  if (options.isError)
    return (
      <div className="p-4">
        <Alert variant="destructive">
          <TriangleAlertIcon />
          <AlertTitle>初始化失败</AlertTitle>
          <AlertDescription>{errorMessage(options.error)}</AlertDescription>
        </Alert>
      </div>
    )
  return <RunView savedOptions={options.data} />
}

function RunView({ savedOptions }: { savedOptions: Options }) {
  const form = useForm<Options>({
    resolver: zodResolver(optionsFormSchema),
    defaultValues: savedOptions,
    mode: "onChange",
  })
  const running = useEngineStore((s) => s.running)
  const paused = useEngineStore((s) => s.paused)

  const sets = useTargetSets()
  const windows = useWindows()
  const [pickedSet, setPickedSet] = useState("")
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const targetSet = resolveTargetSet(sets.data, pickedSet)

  // Write edits back to config.json, debounced. Comparing against the last saved
  // snapshot (rather than reacting to every change) keeps the initial fill and
  // invalid intermediate input from being saved.
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined
    let saved = JSON.stringify(savedOptions)
    const unsubscribe = form.subscribe({
      formState: { values: true },
      callback: ({ values }) => {
        clearTimeout(timer)
        timer = setTimeout(() => {
          const parsed = optionsSchema.safeParse(values)
          if (!parsed.success) return
          const next = JSON.stringify(parsed.data)
          if (next === saved) return
          saved = next
          // a read-only install directory is not worth interrupting the user for
          saveOptions(parsed.data).catch(() => {})
        }, SAVE_DEBOUNCE_MS)
      },
    })
    return () => {
      unsubscribe()
      clearTimeout(timer)
    }
  }, [form, savedOptions])

  const start = () => {
    if (!selected.size) {
      notify.error("请先选择至少一个游戏窗口")
      return
    }
    if (!targetSet) {
      notify.error("请选择目标集")
      return
    }
    void form.handleSubmit(
      async (options) => {
        try {
          await startEngine({
            windowHandles: [...selected],
            targetSet,
            options,
          })
          notify.ok("引擎已启动")
          // The scatter plot draws its axes at the real window resolution. With
          // several windows of different sizes, take the largest so no point is cut off.
          const chosen = (windows.data ?? []).filter((w) =>
            selected.has(w.handle)
          )
          useEngineStore.getState().resetRun(
            chosen.length
              ? {
                  width: Math.max(...chosen.map((w) => w.width)),
                  height: Math.max(...chosen.map((w) => w.height)),
                }
              : null
          )
        } catch (e) {
          notify.error("启动失败:" + errorMessage(e))
        }
      },
      () => notify.error("运行参数有误,请检查标红的字段")
    )()
  }

  const stop = () =>
    stopEngine().catch((e) => notify.error("停止失败:" + errorMessage(e)))

  // The pause only takes effect at the next round boundary, so the button label
  // follows the engine state from the event stream instead of flipping on click.
  const togglePause = () =>
    (paused ? resumeEngine() : pauseEngine()).catch((e) =>
      notify.error((paused ? "继续" : "暂停") + "失败:" + errorMessage(e))
    )

  return (
    <div className="grid min-h-0 flex-1 grid-cols-1 gap-3.5 overflow-y-auto p-3.5 min-[720px]:grid-cols-2 min-[1080px]:grid-cols-[350px_320px_1fr] min-[1080px]:overflow-hidden">
      <Card className="min-h-0">
        <CardHeader className="border-b">
          <CardTitle>控制</CardTitle>
          <CardAction className="flex gap-2">
            <Button type="button" disabled={running} onClick={start}>
              <PlayIcon data-icon="inline-start" />
              启动
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={!running}
              onClick={togglePause}
            >
              {paused ? (
                <PlayIcon data-icon="inline-start" />
              ) : (
                <PauseIcon data-icon="inline-start" />
              )}
              {paused ? "继续" : "暂停"}
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={!running}
              onClick={stop}
            >
              <SquareIcon data-icon="inline-start" />
              停止
            </Button>
          </CardAction>
        </CardHeader>
        <CardContent className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto">
          {running && (
            <Alert>
              <LockIcon />
              <AlertDescription>
                运行中已锁定
              </AlertDescription>
            </Alert>
          )}
          {/* Everything below is a snapshot taken at start; editing it mid-run
              would not affect the current run, so it is locked instead. */}
          <fieldset disabled={running} className="min-w-0 disabled:opacity-60">
            <OptionsForm form={form} disabled={running} />
          </fieldset>
          <Alert>
            <TriangleAlertIcon />
            <AlertDescription>
              阴阳师以管理员运行,本程序须以管理员启动,否则后台点击会被静默丢弃。
            </AlertDescription>
          </Alert>
        </CardContent>
      </Card>

      <fieldset
        disabled={running}
        className="flex min-h-0 min-w-0 flex-col gap-3.5 disabled:opacity-60 min-[1080px]:overflow-y-auto"
      >
        <TargetSetCard value={targetSet} onChange={setPickedSet} />
        <WindowList
          selected={selected}
          onChange={setSelected}
          disabled={running}
        />
      </fieldset>

      <LogPanel />
    </div>
  )
}
