import { expect, test } from "bun:test"

import { dragRect, toNaturalRect, toNormalizedPoint } from "./geometry"

test("dragRect normalises a drag towards the top-left", () => {
  expect(dragRect({ x: 100, y: 80 }, { x: 40, y: 20 })).toEqual({
    x: 40,
    y: 20,
    w: 60,
    h: 60,
  })
})

test("toNaturalRect scales by the natural/displayed ratio per axis", () => {
  // 1200x600 screenshot displayed at half size
  const r = toNaturalRect(
    { x: 10, y: 20, w: 100, h: 50 },
    { width: 600, height: 300 },
    { width: 1200, height: 600 }
  )
  expect(r).toEqual({ x: 20, y: 40, w: 200, h: 100 })
})

test("toNaturalRect rounds to whole pixels", () => {
  const r = toNaturalRect(
    { x: 10, y: 10, w: 10, h: 10 },
    { width: 300, height: 300 },
    { width: 1000, height: 1000 }
  )
  expect(r).toEqual({ x: 33, y: 33, w: 33, h: 33 })
})

test("toNaturalRect rejects a selection under 5px on either side", () => {
  const shown = { width: 600, height: 300 }
  const natural = { width: 1200, height: 600 }
  expect(
    toNaturalRect({ x: 0, y: 0, w: 4.9, h: 50 }, shown, natural)
  ).toBeNull()
  expect(
    toNaturalRect({ x: 0, y: 0, w: 50, h: 4.9 }, shown, natural)
  ).toBeNull()
  expect(
    toNaturalRect({ x: 0, y: 0, w: 5, h: 5 }, shown, natural)
  ).not.toBeNull()
})

test("toNormalizedPoint keeps 4 decimals", () => {
  expect(
    toNormalizedPoint({ x: 100, y: 100 }, { width: 300, height: 700 })
  ).toEqual([0.3333, 0.1429])
})

test("toNormalizedPoint clamps positions outside the image", () => {
  const size = { width: 600, height: 300 }
  expect(toNormalizedPoint({ x: -20, y: 400 }, size)).toEqual([0, 1])
})
