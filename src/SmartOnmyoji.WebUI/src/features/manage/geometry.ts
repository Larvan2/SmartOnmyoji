// Coordinate maths for the capture editor. The screenshot is shown scaled to fit,
// so pointer positions are in displayed pixels and have to be mapped either to the
// screenshot's natural pixels (template crop) or to 0~1 of the client area (click points).

export interface Rect {
  x: number
  y: number
  w: number
  h: number
}

/** Selections smaller than this (displayed px) on either side count as a stray click. */
const MIN_SELECTION = 5

export function clamp(v: number, lo: number, hi: number) {
  return Math.max(lo, Math.min(hi, v))
}

/** Rectangle spanned by a drag from `start` to `current`, in whichever direction. */
export function dragRect(
  start: { x: number; y: number },
  current: { x: number; y: number }
): Rect {
  return {
    x: Math.min(start.x, current.x),
    y: Math.min(start.y, current.y),
    w: Math.abs(current.x - start.x),
    h: Math.abs(current.y - start.y),
  }
}

/**
 * Maps a selection in displayed pixels to the screenshot's natural pixels.
 * Returns null when the selection is too small to be intentional.
 */
export function toNaturalRect(
  shown: Rect,
  shownSize: { width: number; height: number },
  naturalSize: { width: number; height: number }
): Rect | null {
  if (shown.w < MIN_SELECTION || shown.h < MIN_SELECTION) return null
  const rx = naturalSize.width / shownSize.width
  const ry = naturalSize.height / shownSize.height
  return {
    x: Math.round(shown.x * rx),
    y: Math.round(shown.y * ry),
    w: Math.round(shown.w * rx),
    h: Math.round(shown.h * ry),
  }
}

/** Client-area normalised point ([x, y] in 0~1, 4 decimals) for a displayed position. */
export function toNormalizedPoint(
  shown: { x: number; y: number },
  shownSize: { width: number; height: number }
): [number, number] {
  const round4 = (v: number) => Math.round(v * 10000) / 10000
  return [
    round4(clamp(shown.x / shownSize.width, 0, 1)),
    round4(clamp(shown.y / shownSize.height, 0, 1)),
  ]
}
