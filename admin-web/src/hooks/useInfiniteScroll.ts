import { useCallback, useRef } from 'react'

interface Options {
  /** Whether another page exists to fetch. */
  hasNextPage: boolean
  /** True while a page fetch is already in flight (prevents duplicate loads). */
  isFetchingNextPage: boolean
  /** Loads the next page. */
  fetchNextPage: () => void
  /**
   * How far ahead of the sentinel to begin loading, as a CSS margin string.
   * The default pulls the next page while the user is still ~one viewport away
   * from the end (the "load before you hit the bottom" behaviour).
   */
  rootMargin?: string
}

/**
 * Reusable infinite-scroll trigger built on IntersectionObserver.
 *
 * Attach the returned ref to a small sentinel element rendered at the end of a
 * list. When that sentinel scrolls into view (within `rootMargin`), the next
 * page is fetched — works against the nearest scroll container or the viewport.
 *
 * Usage:
 *   const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })
 *   ...
 *   <div ref={sentinelRef} />
 */
export function useInfiniteScroll({
  hasNextPage,
  isFetchingNextPage,
  fetchNextPage,
  rootMargin = '0px 0px 400px 0px',
}: Options) {
  const observerRef = useRef<IntersectionObserver | null>(null)

  // Callback ref so the observer re-binds whenever the sentinel node changes.
  return useCallback(
    (node: HTMLElement | null) => {
      observerRef.current?.disconnect()
      if (!node) return

      observerRef.current = new IntersectionObserver(
        (entries) => {
          if (entries[0]?.isIntersecting && hasNextPage && !isFetchingNextPage) {
            fetchNextPage()
          }
        },
        { rootMargin },
      )
      observerRef.current.observe(node)
    },
    [hasNextPage, isFetchingNextPage, fetchNextPage, rootMargin],
  )
}
