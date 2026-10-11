import type { Size } from "@/api/schemas"
import type { ClickPoint } from "@/stores/engine"

// Target name → stable colour, shared by the scatter plot and the bar chart so each
// serves as the other's legend; wraps around once the palette is exhausted.
const PALETTE = [
  "#007aff",
  "#16a34a",
  "#d97706",
  "#dc2626",
  "#db2777",
  "#8b5cf6",
  "#0891b2",
  "#65a30d",
]
const colorCache = new Map<string, string>()
function colorFor(name: string) {
  let c = colorCache.get(name)
  if (!c) {
    c = PALETTE[colorCache.size % PALETTE.length]
    colorCache.set(name, c)
  }
  return c
}

/** Sizes the backing store for devicePixelRatio; the returned ctx draws in CSS pixels. */
function setupCanvas(canvas: HTMLCanvasElement) {
  const dpr = window.devicePixelRatio || 1
  // clientWidth/Height, not getBoundingClientRect: the dialog scales in on open
  // and a transformed rect would size the canvas 5% too small.
  const w = Math.max(1, canvas.clientWidth)
  const h = Math.max(1, canvas.clientHeight)
  canvas.width = Math.round(w * dpr)
  canvas.height = Math.round(h * dpr)
  const ctx = canvas.getContext("2d")!
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.clearRect(0, 0, w, h)
  return { ctx, w, h }
}

function themeColors() {
  const s = getComputedStyle(document.documentElement)
  const get = (name: string, fallback: string) =>
    s.getPropertyValue(name).trim() || fallback
  return {
    text: get("--foreground", "#1f2430"),
    muted: get("--muted-foreground", "#6b7280"),
    border: get("--border", "#e2e5ea"),
    accent: get("--primary", "#007aff"),
  }
}

function drawEmpty(
  ctx: CanvasRenderingContext2D,
  w: number,
  h: number,
  color: string
) {
  ctx.fillStyle = color
  ctx.font = "12px sans-serif"
  ctx.textAlign = "center"
  ctx.textBaseline = "middle"
  ctx.fillText("本次运行暂无点击数据", w / 2, h / 2)
}

function fitLabel(ctx: CanvasRenderingContext2D, text: string, maxW: number) {
  let s = text
  if (ctx.measureText(s).width <= maxW - 4) return s
  while (s.length > 1 && ctx.measureText(s + "…").width > maxW - 4)
    s = s.slice(0, -1)
  return s + "…"
}

/**
 * Click scatter plot. The axes span the run's real client-area resolution rather
 * than the min/max of the points, so a cluster in one corner still reads as "that
 * corner of the window"; letterboxed to the window's aspect ratio. Canvas y grows
 * downward, same as screen coordinates.
 */
export function drawScatter(
  canvas: HTMLCanvasElement,
  pts: ClickPoint[],
  clientSize: Size | null
) {
  const { ctx, w, h } = setupCanvas(canvas)
  const tc = themeColors()
  if (!pts.length) {
    drawEmpty(ctx, w, h, tc.muted)
    return
  }

  const pad = 14
  const availW = w - 2 * pad
  const availH = h - 2 * pad

  let boundsW: number
  let boundsH: number
  let originX = 0
  let originY = 0
  let labelResolution = true
  if (clientSize && clientSize.width > 0 && clientSize.height > 0) {
    boundsW = clientSize.width
    boundsH = clientSize.height
  } else {
    // Fallback (page reloaded mid-run, so the chosen windows are unknown): fit the
    // data, with a minimum span so a single cluster is not blown up.
    labelResolution = false
    const xs = pts.map((p) => p.x)
    const ys = pts.map((p) => p.y)
    let minX = Math.min(...xs)
    let maxX = Math.max(...xs)
    let minY = Math.min(...ys)
    let maxY = Math.max(...ys)
    const spanMin = 80
    if (maxX - minX < spanMin) {
      const c = (minX + maxX) / 2
      minX = c - spanMin / 2
      maxX = c + spanMin / 2
    }
    if (maxY - minY < spanMin) {
      const c = (minY + maxY) / 2
      minY = c - spanMin / 2
      maxY = c + spanMin / 2
    }
    originX = minX
    originY = minY
    boundsW = maxX - minX
    boundsH = maxY - minY
  }

  const scale = Math.min(availW / boundsW, availH / boundsH)
  const plotW = boundsW * scale
  const plotH = boundsH * scale
  const offX = pad + (availW - plotW) / 2
  const offY = pad + (availH - plotH) / 2

  ctx.strokeStyle = tc.border
  ctx.lineWidth = 1
  ctx.strokeRect(offX, offY, plotW, plotH)

  ctx.globalAlpha = 0.75
  for (const p of pts) {
    ctx.beginPath()
    ctx.arc(
      offX + (p.x - originX) * scale,
      offY + (p.y - originY) * scale,
      2.5,
      0,
      Math.PI * 2
    )
    ctx.fillStyle = colorFor(p.name)
    ctx.fill()
  }
  ctx.globalAlpha = 1

  ctx.fillStyle = tc.muted
  ctx.font = "10px sans-serif"
  ctx.textAlign = "left"
  ctx.textBaseline = "top"
  const label = labelResolution
    ? `窗口客户区 ${Math.round(boundsW)}×${Math.round(boundsH)}`
    : `x:${Math.round(originX)}~${Math.round(originX + boundsW)}  y:${Math.round(originY)}~${Math.round(originY + boundsH)}(未知窗口分辨率,按点击范围显示)`
  ctx.fillText(label, pad + 2, 2)
}

function drawBars(
  canvas: HTMLCanvasElement,
  labels: string[],
  values: number[],
  colorOf?: (label: string) => string
) {
  const { ctx, w, h } = setupCanvas(canvas)
  const tc = themeColors()
  if (!values.length || values.every((v) => v === 0)) {
    drawEmpty(ctx, w, h, tc.muted)
    return
  }

  const padL = 28
  const padR = 10
  const padT = 14
  const padB = 34
  const plotW = w - padL - padR
  const plotH = h - padT - padB
  const maxV = Math.max(...values)

  ctx.strokeStyle = tc.border
  ctx.lineWidth = 1
  ctx.beginPath()
  ctx.moveTo(padL, padT)
  ctx.lineTo(padL, padT + plotH)
  ctx.lineTo(padL + plotW, padT + plotH)
  ctx.stroke()

  ctx.fillStyle = tc.muted
  ctx.font = "10px sans-serif"
  ctx.textAlign = "right"
  ctx.textBaseline = "middle"
  ctx.fillText(String(maxV), padL - 4, padT + 4)
  ctx.fillText("0", padL - 4, padT + plotH)

  const bw = plotW / values.length
  const barW = Math.min(bw * 0.7, 46)
  for (let i = 0; i < values.length; i++) {
    const bh = (values[i] / maxV) * plotH
    const cx = padL + bw * (i + 0.5)
    ctx.fillStyle = colorOf ? colorOf(labels[i]) : tc.accent
    ctx.fillRect(cx - barW / 2, padT + plotH - bh, barW, bh)
    if (values[i] > 0) {
      ctx.fillStyle = tc.text
      ctx.textAlign = "center"
      ctx.textBaseline = "bottom"
      ctx.fillText(String(values[i]), cx, padT + plotH - bh - 2)
    }
    ctx.fillStyle = tc.muted
    ctx.textAlign = "center"
    ctx.textBaseline = "top"
    ctx.fillText(fitLabel(ctx, labels[i], bw), cx, padT + plotH + 5)
  }
}

export function drawTargetBars(canvas: HTMLCanvasElement, pts: ClickPoint[]) {
  const counts = new Map<string, number>()
  for (const p of pts) counts.set(p.name, (counts.get(p.name) ?? 0) + 1)
  drawBars(canvas, [...counts.keys()], [...counts.values()], colorFor)
}

function formatElapsed(ms: number) {
  const s = Math.round(ms / 1000)
  const m = Math.floor(s / 60)
  return m > 0 ? `${m}m${String(s % 60).padStart(2, "0")}` : `${s}s`
}

/** Clicks over time, bucketed relative to the run's first click. */
export function drawTimeBars(canvas: HTMLCanvasElement, pts: ClickPoint[]) {
  if (!pts.length) {
    drawBars(canvas, [], [])
    return
  }
  const ts = pts.map((p) => p.t)
  const minT = Math.min(...ts)
  const span = Math.max(1, Math.max(...ts) - minT)
  // ~30s per bucket, clamped to 4~12 buckets
  const bins = Math.min(12, Math.max(4, Math.ceil(span / 30000)))
  const binMs = span / bins
  const values = new Array<number>(bins).fill(0)
  for (const t of ts)
    values[Math.min(bins - 1, Math.floor((t - minT) / binMs))]++
  drawBars(
    canvas,
    values.map((_, i) => formatElapsed(i * binMs)),
    values
  )
}
