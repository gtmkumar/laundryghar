/**
 * useNewItemDetector — tracks a stream of ids across polls and reports which
 * ones are newly-seen, WITHOUT firing on the first load (mount baseline).
 *
 * The first time it sees a non-empty list it records every id as a baseline
 * and returns no "new" ids (so we never chime/highlight on page open). On
 * subsequent polls, any id not in the seen-set is returned as new (and added
 * to the set). Highlighted ids auto-expire after `highlightMs` so the pulse
 * animation lasts roughly one poll cycle.
 *
 * This is intentionally agnostic of WHAT the items are (orders, pickups) so the
 * Orders page and the dashboard panels can share it.
 */
import { useCallback, useEffect, useRef, useState } from 'react'

interface Options {
  /** How long an id stays "highlighted" after appearing (ms). Default 6000. */
  highlightMs?: number
  /** Called once per genuinely-new id (after the baseline). */
  onNew?: (id: string) => void
}

interface Result {
  /** Set of ids currently in the highlight (pulse) window. */
  highlightedIds: Set<string>
  /** Convenience predicate for render. */
  isHighlighted: (id: string) => boolean
}

export function useNewItemDetector(ids: string[], opts: Options = {}): Result {
  const { highlightMs = 6000, onNew } = opts
  const seenRef = useRef<Set<string> | null>(null)
  const [highlightedIds, setHighlighted] = useState<Set<string>>(new Set())
  const timersRef = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map())
  // Keep the latest onNew without re-running the effect when the caller passes
  // a fresh closure each render.
  const onNewRef = useRef(onNew)
  useEffect(() => {
    onNewRef.current = onNew
  }, [onNew])

  useEffect(() => {
    // Establish the baseline on the first non-empty snapshot — no "new" events.
    if (seenRef.current === null) {
      if (ids.length === 0) return // wait for the first real list
      seenRef.current = new Set(ids)
      return
    }

    const seen = seenRef.current
    const fresh: string[] = []
    for (const id of ids) {
      if (!seen.has(id)) {
        seen.add(id)
        fresh.push(id)
      }
    }
    if (fresh.length === 0) return

    fresh.forEach((id) => onNewRef.current?.(id))

    setHighlighted((prev) => {
      const next = new Set(prev)
      fresh.forEach((id) => next.add(id))
      return next
    })

    // Schedule each new id to leave the highlight window.
    fresh.forEach((id) => {
      const existing = timersRef.current.get(id)
      if (existing) clearTimeout(existing)
      const timer = setTimeout(() => {
        setHighlighted((prev) => {
          if (!prev.has(id)) return prev
          const next = new Set(prev)
          next.delete(id)
          return next
        })
        timersRef.current.delete(id)
      }, highlightMs)
      timersRef.current.set(id, timer)
    })
  }, [ids, highlightMs])

  // Clear timers on unmount.
  useEffect(() => {
    const timers = timersRef.current
    return () => {
      timers.forEach((t) => clearTimeout(t))
      timers.clear()
    }
  }, [])

  const isHighlighted = useCallback((id: string) => highlightedIds.has(id), [highlightedIds])

  return { highlightedIds, isHighlighted }
}
