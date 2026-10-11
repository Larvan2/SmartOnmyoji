import { expect, test } from "bun:test"

import { createReplayFilter } from "./replay"

function deliver(
  filter: ReturnType<typeof createReplayFilter>,
  events: string[]
) {
  return events.filter((e) => !filter.isReplay(e))
}

test("the first connection's catch-up replay is delivered in full", () => {
  const filter = createReplayFilter(10)
  filter.opened()
  expect(deliver(filter, ["a", "b", "c"])).toEqual(["a", "b", "c"])
})

test("events replayed after a reconnect are dropped", () => {
  const filter = createReplayFilter(10)
  filter.opened()
  deliver(filter, ["a", "b", "c"])

  filter.opened()
  expect(deliver(filter, ["a", "b", "c"])).toEqual([])
})

test("events missed while disconnected come through after the replayed ones", () => {
  const filter = createReplayFilter(10)
  filter.opened()
  deliver(filter, ["a", "b"])

  filter.opened()
  expect(deliver(filter, ["a", "b", "c", "d"])).toEqual(["c", "d"])
})

test("a second reconnect also drops what arrived after the first one", () => {
  const filter = createReplayFilter(10)
  filter.opened()
  deliver(filter, ["a"])
  filter.opened()
  deliver(filter, ["a", "b"])

  filter.opened()
  expect(deliver(filter, ["a", "b", "c"])).toEqual(["c"])
})

test("only the last `memory` events are remembered", () => {
  const filter = createReplayFilter(2)
  filter.opened()
  deliver(filter, ["a", "b", "c"])

  filter.opened()
  // "a" has been forgotten, so it is indistinguishable from a new event
  expect(deliver(filter, ["a"])).toEqual(["a"])
})
