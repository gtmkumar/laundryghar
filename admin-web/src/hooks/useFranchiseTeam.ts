import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { getFranchiseTeam, type TeamScope } from '@/api/franchiseTeam'

const PREVIEW_SIZE = 5
const DRAWER_PAGE_SIZE = 30

/**
 * Lightweight hovercard preview: the first {PREVIEW_SIZE} rows + total count.
 * Lazy (only fires when `enabled`) and cached for 30s so re-hover is instant.
 */
export function useTeamPreview(scope: TeamScope, franchiseId: string, enabled: boolean) {
  return useQuery({
    queryKey: ['franchise-team', scope, franchiseId, 'preview'],
    queryFn: () => getFranchiseTeam(scope, franchiseId, 1, PREVIEW_SIZE),
    enabled: enabled && !!franchiseId,
    staleTime: 30_000,
  })
}

/** The full infinite-scroll list for the drawer. */
export function useTeamInfinite(scope: TeamScope, franchiseId: string, enabled: boolean) {
  return useInfiniteQuery({
    queryKey: ['franchise-team', scope, franchiseId, 'list'],
    queryFn: ({ pageParam }) => getFranchiseTeam(scope, franchiseId, pageParam, DRAWER_PAGE_SIZE),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => (lastPage.hasNextPage ? allPages.length + 1 : undefined),
    enabled: enabled && !!franchiseId,
  })
}
