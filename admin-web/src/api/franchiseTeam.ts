import type { PaginatedList } from '@/types/api'
import { getStores } from './tenancy'
import { getRiders } from './riders'
import { getAccessPeople } from './accessControl'

/** The three team scopes surfaced by a franchise card's stat tiles. */
export type TeamScope = 'stores' | 'staff' | 'riders'

/** A single row, normalised across stores / staff / riders so one renderer fits all. */
export interface TeamRow {
  id: string
  title: string
  subtitle: string
  status: string
  initials?: string | null
}

export const TEAM_LABEL: Record<TeamScope, string> = {
  stores: 'Stores',
  staff: 'Staff',
  riders: 'Riders',
}

function remap<T>(pl: PaginatedList<T>, fn: (x: T) => TeamRow): PaginatedList<TeamRow> {
  return { ...pl, list: pl.list.map(fn) }
}

function initialsOf(name: string): string {
  const parts = name.trim().split(/[\s.@]+/).filter(Boolean)
  if (parts.length === 0) return '?'
  return ((parts[0][0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase()
}

/**
 * Fetch one page of a franchise's stores / staff / riders, normalised to TeamRow.
 * Reuses the existing per-scope endpoints (each already filters by franchiseId).
 */
export async function getFranchiseTeam(
  scope: TeamScope,
  franchiseId: string,
  page: number,
  pageSize: number,
): Promise<PaginatedList<TeamRow>> {
  if (scope === 'stores') {
    const pl = await getStores({ franchiseId, page, pageSize })
    return remap(pl, (s) => ({
      id: s.id,
      title: s.name,
      subtitle: [s.city, s.storeType].filter(Boolean).join(' · '),
      status: s.status,
    }))
  }

  if (scope === 'riders') {
    const pl = await getRiders(page, pageSize, { franchiseId })
    return remap(pl, (r) => {
      const name = r.riderName ?? r.riderCode
      return {
        id: r.id,
        title: name,
        subtitle: [r.vehicleType, r.isOnDuty ? 'on duty' : null].filter(Boolean).join(' · '),
        status: r.status,
        initials: initialsOf(name),
      }
    })
  }

  // staff — the People endpoint scoped to this franchise (excludes the owner & riders)
  const pg = await getAccessPeople({ franchiseId, page, pageSize })
  return remap(pg.people, (p) => ({
    id: p.id,
    title: p.name,
    subtitle: p.roleName,
    status: p.status,
    initials: p.initials,
  }))
}
