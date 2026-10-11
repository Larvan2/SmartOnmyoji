/**
 * Drops events that the backend replays after the event stream reconnects.
 *
 * Every (re)subscription makes the backend send its recent events again. On the
 * first connection that is how the page catches up; after a reconnect most of them
 * were already shown. Raw payloads carry a high-resolution timestamp, so an
 * identical payload is the same event and can be recognised by plain equality.
 */
export function createReplayFilter(memory: number) {
  const seen: string[] = []
  let replayed: Set<string> | null = null

  return {
    /** Call each time the stream opens. */
    opened() {
      replayed = new Set(seen)
    },

    /** True when `raw` was already delivered before the stream last opened. */
    isReplay(raw: string) {
      if (replayed?.has(raw)) return true
      // The replay comes first and in order, so the first unseen event ends it.
      replayed = null
      seen.push(raw)
      if (seen.length > memory) seen.shift()
      return false
    },
  }
}
