import { useState } from 'react'
import { Loader2, ShieldCheck, Store, Users, Bike, ArrowRight, Plus } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { PaginatedList, AccessFranchise } from '@/types/api'
import { useOnboardingUi } from '@/stores/onboardingStore'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { FranchiseStatTile } from './FranchiseStatTile'
import { FranchiseTeamDrawer } from './FranchiseTeamDrawer'
import type { TeamScope } from '@/api/franchiseTeam'

type TeamTarget = { id: string; name: string; scope: TeamScope }

interface Props {
  query: {
    data?: { pages: PaginatedList<AccessFranchise>[] }
    isLoading: boolean
    isError: boolean
    error?: unknown
    hasNextPage: boolean
    isFetchingNextPage: boolean
    fetchNextPage: () => void
  }
}

function rupeesLakh(v: number): string {
  if (v >= 10_000_000) return `₹${(v / 10_000_000).toFixed(2).replace(/\.00$/, '')}Cr`
  if (v >= 100_000) return `₹${(v / 100_000).toFixed(1).replace(/\.0$/, '')}L`
  return `₹${v.toLocaleString('en-IN')}`
}

function FranchiseCard({
  f,
  onOpen,
  onOpenTeam,
}: {
  f: AccessFranchise
  onOpen: (id: string) => void
  onOpenTeam: (target: TeamTarget) => void
}) {
  const onboarding = f.status === 'Onboarding'
  const company = f.ownershipType === 'company'
  return (
    <div className="rounded-2xl border border-gray-200 bg-white p-5 shadow-[0_1px_2px_rgba(16,16,16,0.04)]">
      <div className="flex items-start justify-between gap-2">
        <div>
          <h3 className="text-base font-bold text-gray-900">{f.name}</h3>
          <p className="text-xs text-gray-400">{f.location} · since {f.sinceYear || '—'}</p>
        </div>
        <span
          className={cn(
            'rounded-full px-2 py-0.5 text-[11px] font-semibold',
            company ? 'bg-sky-100 text-sky-700' : 'bg-lg-green/10 text-lg-green',
          )}
        >
          {company ? 'Company' : 'Franchise'}
        </span>
      </div>

      {/* Owner */}
      <div className="mt-4 flex items-center gap-2.5 rounded-xl bg-gray-50 px-3 py-2">
        <span className={cn('flex h-7 w-7 items-center justify-center rounded-full text-xs font-semibold text-white', company ? 'bg-sky-500' : 'bg-lg-green')}>
          {company ? 'C' : f.ownerInitials ?? '—'}
        </span>
        <div className="min-w-0">
          <p className="text-[10px] uppercase tracking-wide text-gray-400">Owner</p>
          <p className="truncate text-sm font-medium text-gray-800">{company ? 'Company-owned' : f.ownerName ?? '—'}</p>
        </div>
        <p className="ml-auto text-right text-sm font-bold text-gray-900">{rupeesLakh(f.revenueMonthly)}<span className="block text-[10px] font-normal text-gray-400">rev/mo</span></p>
      </div>

      {/* Stats — hover to preview, click to open the team drawer */}
      <div className="mt-4 grid grid-cols-3 gap-2 text-center">
        {([
          { icon: Store, scope: 'stores' as const, value: f.storeCount, rowIcon: <Store className="h-3.5 w-3.5" /> },
          { icon: Users, scope: 'staff' as const, value: f.staffCount, rowIcon: <Users className="h-3.5 w-3.5" /> },
          { icon: Bike, scope: 'riders' as const, value: f.riderCount, rowIcon: <Bike className="h-3.5 w-3.5" /> },
        ]).map((s) => (
          <FranchiseStatTile
            key={s.scope}
            icon={s.icon}
            value={s.value}
            scope={s.scope}
            franchiseId={f.id}
            rowIcon={s.rowIcon}
            onOpen={(scope) => onOpenTeam({ id: f.id, name: f.name, scope })}
          />
        ))}
      </div>

      {/* Footer */}
      <div className="mt-4 flex items-center justify-between">
        <span className="inline-flex items-center gap-1.5 text-xs font-medium">
          <span className={cn('h-1.5 w-1.5 rounded-full', onboarding ? 'bg-amber-500' : 'bg-emerald-500')} />
          <span className={onboarding ? 'text-amber-700' : 'text-emerald-700'}>{f.status}</span>
        </span>
        <button
          type="button"
          onClick={() => onOpen(f.id)}
          className={cn(
            'inline-flex items-center gap-1 text-xs font-semibold transition-all hover:gap-1.5',
            onboarding ? 'text-amber-700' : 'text-lg-green',
          )}
        >
          {onboarding ? 'Continue onboarding' : 'Manage team'} <ArrowRight className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  )
}

export function FranchisesTab({ query }: Props) {
  const { data, isLoading, isError, error, hasNextPage, isFetchingNextPage, fetchNextPage } = query
  const openOnboarding = useOnboardingUi((s) => s.openOnboarding)
  const workspaceOpen = useOnboardingUi((s) => s.open)

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  const [team, setTeam] = useState<TeamTarget | null>(null)

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading franchises…
      </div>
    )
  }
  if (isError || !data) {
    if (isForbiddenError(error)) return <ForbiddenState message="You don’t have access to franchises." />
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load franchises.</div>
  }

  const franchises = data.pages.flatMap((p) => p.list)
  const total = data.pages[0]?.totalCount ?? franchises.length

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between gap-3">
        <p className="text-sm text-gray-500">{total} franchise{total === 1 ? '' : 's'}</p>
        <button
          type="button"
          onClick={() => openOnboarding(null)}
          className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-3.5 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
        >
          <Plus className="h-4 w-4" /> Onboard franchise
        </button>
      </div>

      {/* Two-tier banner */}
      <div className="flex items-start gap-3 rounded-2xl border border-lg-green/20 bg-lg-green/5 px-4 py-3">
        <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-lg-green" />
        <p className="text-sm text-gray-600">
          <span className="font-semibold text-gray-900">Two-tier access model.</span>{' '}
          HQ manages enterprise staff and sets the permission ceiling for each franchise role. Franchise Owners invite and manage their own Store Managers, Staff and Riders — but only within that ceiling, and only for their own stores.
        </p>
      </div>

      {/* Cards — reflow to a single column in workspace mode so they stay readable */}
      <div className={cn('grid gap-4', workspaceOpen ? 'grid-cols-1' : 'grid-cols-1 sm:grid-cols-2 xl:grid-cols-3')}>
        {franchises.map((f) => (
          <FranchiseCard key={f.id} f={f} onOpen={openOnboarding} onOpenTeam={setTeam} />
        ))}
      </div>

      {/* Infinite-scroll sentinel + loading-more indicator */}
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}

      <FranchiseTeamDrawer
        open={team !== null}
        franchiseId={team?.id ?? null}
        franchiseName={team?.name ?? ''}
        scope={team?.scope ?? 'stores'}
        onClose={() => setTeam(null)}
      />
    </div>
  )
}
