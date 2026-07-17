import type { QueryClient, QueryKey } from '@tanstack/react-query'

import { apiErrorMessage, apiErrorStatus } from '@/lib/apiError'
import { showToast } from '@/stores/toastStore'

/**
 * Helpers for optimistic cache updates (TanStack Query v5).
 *
 * House pattern for a mutation that should feel instant (toggles, deletes,
 * inline edits):
 *
 *   onMutate:  snapshotAndUpdate(qc, prefixes, mapper)  → return the context
 *   onError:   rollbackWithToast(ctx, error)            → visible, honest rollback
 *   onSettled: invalidate the same prefixes             → reconcile with the server
 *
 * The cache writers below understand every list shape this app caches:
 *   - a bare array                       T[]
 *   - a paginated list                   { list: T[], … }
 *   - an infinite query                  { pages: Array<{ list: T[], … }>, … }
 * Detail objects (a single entity keyed by id) can be patched with
 * `patchEntity` instead.
 */

type PagedList = { list: unknown[] }
type InfiniteData = { pages: PagedList[] }

function isPagedList(data: unknown): data is PagedList {
  return !!data && typeof data === 'object' && Array.isArray((data as PagedList).list)
}

function isInfiniteData(data: unknown): data is InfiniteData {
  return (
    !!data &&
    typeof data === 'object' &&
    Array.isArray((data as InfiniteData).pages) &&
    (data as InfiniteData).pages.every(isPagedList)
  )
}

/** Apply `mapList` to the item array inside any cached list shape; leave unknown shapes untouched. */
function mapCachedList(data: unknown, mapList: (items: unknown[]) => unknown[]): unknown {
  if (Array.isArray(data)) return mapList(data)
  if (isInfiniteData(data)) {
    return { ...data, pages: data.pages.map((p) => ({ ...p, list: mapList(p.list) })) }
  }
  if (isPagedList(data)) return { ...data, list: mapList(data.list) }
  return data
}

export interface OptimisticContext {
  /** Restores every cache entry touched by the optimistic write. */
  rollback: () => void
}

/**
 * Lowest-level primitive: cancels in-flight fetches for the given key prefixes
 * (so a slow response can't clobber the optimistic write), snapshots every
 * matching cache entry, applies `transform` to each, and returns a rollback
 * that restores the snapshots. Call from `onMutate` and return the context.
 * Use this directly for caches that aren't list-shaped (`{rows}`, `{modules}`…);
 * prefer the list wrappers below otherwise.
 */
export async function snapshotAndSet(
  qc: QueryClient,
  keyPrefixes: readonly QueryKey[],
  transform: (data: unknown) => unknown,
): Promise<OptimisticContext> {
  await Promise.all(keyPrefixes.map((queryKey) => qc.cancelQueries({ queryKey })))

  const snapshots: Array<[QueryKey, unknown]> = []
  for (const queryKey of keyPrefixes) {
    for (const [key, data] of qc.getQueriesData({ queryKey })) {
      if (data === undefined) continue
      snapshots.push([key, data])
      qc.setQueryData(key, transform(data))
    }
  }

  return {
    rollback: () => {
      for (const [key, data] of snapshots) qc.setQueryData(key, data)
    },
  }
}

/** `snapshotAndSet` specialised to the item array inside any cached list shape. */
export function snapshotAndUpdate(
  qc: QueryClient,
  keyPrefixes: readonly QueryKey[],
  mapList: (items: unknown[]) => unknown[],
): Promise<OptimisticContext> {
  return snapshotAndSet(qc, keyPrefixes, (data) => mapCachedList(data, mapList))
}

/**
 * Standard onError half of the pattern: restore the snapshots and surface a
 * visible rollback toast. 403s are skipped — the api client's interceptor
 * already toasts permission denials globally.
 */
export function rollbackWithToast(ctx: OptimisticContext | undefined, error: unknown) {
  ctx?.rollback()
  if (apiErrorStatus(error) !== 403) {
    showToast('error', apiErrorMessage(error, 'Change failed — reverted.'))
  }
}

/** Optimistically patch the row with `id` in every matching cached list. */
export function patchListItem<T extends { id: string }>(
  qc: QueryClient,
  keyPrefixes: readonly QueryKey[],
  id: string,
  patch: Partial<T>,
): Promise<OptimisticContext> {
  return snapshotAndUpdate(qc, keyPrefixes, (items) =>
    items.map((it) =>
      it && typeof it === 'object' && (it as T).id === id ? { ...it, ...patch } : it,
    ),
  )
}

/** Optimistically remove the row with `id` from every matching cached list. */
export function removeListItem(
  qc: QueryClient,
  keyPrefixes: readonly QueryKey[],
  id: string,
): Promise<OptimisticContext> {
  return snapshotAndUpdate(qc, keyPrefixes, (items) =>
    items.filter((it) => !(it && typeof it === 'object' && (it as { id?: string }).id === id)),
  )
}

/**
 * Optimistically patch a single cached detail object (exact key match).
 * Snapshot/rollback semantics identical to `snapshotAndUpdate`.
 */
export async function patchEntity<T>(
  qc: QueryClient,
  queryKey: QueryKey,
  patch: Partial<T>,
): Promise<OptimisticContext> {
  await qc.cancelQueries({ queryKey })
  const previous = qc.getQueryData<T>(queryKey)
  if (previous !== undefined) qc.setQueryData(queryKey, { ...previous, ...patch })
  return {
    rollback: () => {
      if (previous !== undefined) qc.setQueryData(queryKey, previous)
    },
  }
}
