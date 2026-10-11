import { z } from "zod"

// Mirrors Ui/Contracts.cs and the request records at the end of Ui/ApiEndpoints.cs.
// Window handles travel as hex strings ("0x1A2B").

export const sizeSchema = z.object({ width: z.number(), height: z.number() })
export type Size = z.infer<typeof sizeSchema>

export const windowSchema = z.object({
  handle: z.string(),
  title: z.string(),
  processId: z.number(),
  width: z.number(),
  height: z.number(),
})
export type WindowInfo = z.infer<typeof windowSchema>

export const flagSchema = z.enum([
  "Normal",
  "RoundStart",
  "Once",
  "Skip",
  "Stop",
])
export type TargetFlag = z.infer<typeof flagSchema>

export const targetSetSummarySchema = z.object({
  name: z.string(),
  imageCount: z.number(),
  source: z.string(),
})
export type TargetSetSummary = z.infer<typeof targetSetSummarySchema>

export const targetSetDetailSchema = z.object({
  name: z.string(),
  source: z.string(),
  images: z.array(
    z.object({
      name: z.string(),
      file: z.string(),
      priority: z.number(),
      flag: flagSchema,
      hasClick: z.boolean(),
      baseSize: sizeSchema.nullish(),
    })
  ),
  warnings: z.array(z.string()),
})
export type TargetSetDetail = z.infer<typeof targetSetDetailSchema>

// target.json as served by GET /api/targets/{name}/edit and written back by PUT
// (Core/Targets/TargetSetJson.cs). Loose objects: fields the UI does not edit
// (defaults, matcher) must survive the round trip.
export const targetImageJsonSchema = z.looseObject({
  file: z.string(),
  priority: z.number(),
  flag: flagSchema,
  click: z.looseObject({ clickPos: z.array(z.array(z.number())) }).nullish(),
  baseSize: sizeSchema.nullish(),
})
export type TargetImageJson = z.infer<typeof targetImageJsonSchema>

export const targetSetJsonSchema = z.looseObject({
  name: z.string(),
  images: z.array(targetImageJsonSchema),
})
export type TargetSetJson = z.infer<typeof targetSetJsonSchema>

export const saveImageResultSchema = z.object({
  file: z.string(),
  baseSize: sizeSchema.nullish(),
})

const count = z.number("请输入数字").int("须为整数")
const amount = z.number("请输入数字")

// Shape only. Whether the values make sense is a separate question (see
// optionsFormSchema): config.json may legitimately hold values that are invalid
// for a mode that is not selected, and those must still load and save.
export const optionsSchema = z.object({
  mode: z.enum(["ByRounds", "ByMinutes"]),
  rounds: count,
  perWindowRounds: z.boolean(),
  durationMinutes: amount,
  intervalMin: amount,
  intervalMax: amount,
  matchThreshold: amount,
  // null = leave the game's process priority alone
  priority: z.string().nullable(),
  antiDetection: z.object({
    enabled: z.boolean(),
    clickDeviation: count,
    randomWait: z.object({
      enabled: z.boolean(),
      probability: amount,
      minSeconds: count,
      maxSeconds: count,
      minGapSeconds: count,
    }),
    frequencyCap: z.object({
      enabled: z.boolean(),
      windowMinutes: count,
      maxRounds: count,
      extraWaitSeconds: count,
    }),
    enableRepeatStop: z.boolean(),
    repeatSameTargetStop: count,
  }),
})
export type Options = z.infer<typeof optionsSchema>

/** The rules of Core/Config/EngineOptionsValidation.cs, reported per field. */
export const optionsFormSchema = optionsSchema.superRefine((o, ctx) => {
  const require = (ok: boolean, message: string, ...path: string[]) => {
    if (!ok) ctx.addIssue({ code: "custom", message, path })
  }
  const anti = o.antiDetection
  const rw = anti.randomWait
  const fc = anti.frequencyCap

  if (o.mode === "ByRounds") require(o.rounds > 0, "须大于 0", "rounds")
  else require(o.durationMinutes > 0, "须大于 0", "durationMinutes")

  require(o.intervalMin >= 0, "不能为负", "intervalMin")
  require(o.intervalMax >= o.intervalMin, "上限不能小于下限", "intervalMax")
  require(o.matchThreshold > 0 &&
    o.matchThreshold <= 1, "须在 (0,1]", "matchThreshold")

  require(anti.clickDeviation >=
    0, "不能为负", "antiDetection", "clickDeviation")
  require(anti.repeatSameTargetStop >=
    1, "至少 1", "antiDetection", "repeatSameTargetStop")

  const rwPath = ["antiDetection", "randomWait"]
  require(rw.probability >= 0 &&
    rw.probability <= 1, "须在 [0,1]", ...rwPath, "probability")
  require(rw.minSeconds >= 0, "不能为负", ...rwPath, "minSeconds")
  require(rw.maxSeconds >=
    rw.minSeconds, "上限不能小于下限", ...rwPath, "maxSeconds")
  require(rw.minGapSeconds >= 0, "不能为负", ...rwPath, "minGapSeconds")

  const fcPath = ["antiDetection", "frequencyCap"]
  require(fc.windowMinutes > 0, "须大于 0", ...fcPath, "windowMinutes")
  require(fc.maxRounds > 0, "须大于 0", ...fcPath, "maxRounds")
  require(fc.extraWaitSeconds >= 0, "不能为负", ...fcPath, "extraWaitSeconds")
})

const pointSchema = z.object({ x: z.number(), y: z.number() })
export type Point = z.infer<typeof pointSchema>

// SSE payloads, built in EngineManager.StateEnvelope / LogEnvelope / EventEnvelope.
const at = z.string().optional()
export const engineEventSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("state"),
    running: z.boolean(),
    paused: z.boolean(),
    round: z.number(),
    progress: z.number(),
  }),
  z.object({ type: z.literal("round"), at, round: z.number() }),
  z.object({
    type: z.literal("matched"),
    at,
    name: z.string(),
    x: z.number(),
    y: z.number(),
    score: z.number(),
    thumb: z.string().optional(),
    // click position normalised (0~1) to the thumbnail
    mark: pointSchema.optional(),
  }),
  z.object({
    type: z.literal("clicked"),
    at,
    name: z.string(),
    points: z.array(pointSchema),
  }),
  z.object({
    type: z.literal("waiting"),
    at,
    seconds: z.number(),
    reason: z.string(),
  }),
  z.object({ type: z.literal("progress"), at, percent: z.number() }),
  z.object({ type: z.literal("paused"), at, paused: z.boolean() }),
  z.object({ type: z.literal("stopped"), at, reason: z.string() }),
  z.object({ type: z.literal("error"), at, message: z.string() }),
  z.object({
    type: z.literal("log"),
    at,
    level: z.string().nullish(),
    text: z.string(),
  }),
])
export type EngineEvent = z.infer<typeof engineEventSchema>
