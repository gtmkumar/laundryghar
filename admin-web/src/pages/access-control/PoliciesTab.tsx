import { useMemo, useState } from 'react'
import { ShieldCheck, ShieldX, Globe, Building2, AlertTriangle, Clock } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePolicies, useDecisions, useUpdatePolicy } from '@/hooks/usePolicies'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import { FilterableTable } from '@/components/shared/FilterableTable'
import { FormDrawer } from '@/components/shared/FormDrawer'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import type { AbacPolicy, AbacDecision } from '@/types/api'

interface Props {
  search?: string
}

/**
 * A8.1 / A8.2 — the policy list and the "why was this denied" feed.
 *
 * Replaces the checkbox matrix as the place authorization rules are READ. The matrix can only say
 * whether a role holds a code; it has nowhere to put "…for refunds under ₹5,000", which is exactly
 * the class of rule this table exists to make visible.
 */
export function PoliciesTab({ search }: Props) {
  const [view, setView] = useState<'policies' | 'explain'>('policies')
  const [selected, setSelected] = useState<AbacPolicy | null>(null)

  const { hasPermission } = usePermissions()
  const canEdit = hasPermission('permissions.assign')

  const policies = usePolicies({ search: search?.trim() || undefined })
  const decisions = useDecisions({ deniesOnly: true })
  const update = useUpdatePolicy()

  if (policies.isError && isForbiddenError(policies.error)) return <ForbiddenState />

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        {(['policies', 'explain'] as const).map((v) => (
          <button
            key={v}
            onClick={() => setView(v)}
            className={cn(
              'rounded-md px-3 py-1.5 text-sm font-medium transition',
              view === v ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-600 hover:bg-gray-200',
            )}
          >
            {v === 'policies' ? 'Policies' : 'Why was this denied?'}
          </button>
        ))}
      </div>

      {view === 'policies'
        ? <PolicyList
            query={policies}
            canEdit={canEdit}
            onSelect={setSelected}
          />
        : <ExplainList query={decisions} />}

      <PolicyDrawer
        policy={selected}
        canEdit={canEdit}
        submitting={update.isPending}
        onClose={() => setSelected(null)}
        onSave={async (payload) => {
          try {
            await update.mutateAsync({ id: selected!.id, payload })
            showToast('success', 'Policy updated.')
            setSelected(null)
          } catch {
            // The server refuses a brand's attempt to loosen a platform policy (A8.3). Say exactly
            // that, rather than a generic failure — it is a rule, not an outage.
            showToast(
              'error',
              'Could not update. A brand may tighten a platform policy but never relax one.',
            )
          }
        }}
      />
    </div>
  )
}

// ── Policy list ─────────────────────────────────────────────────────────────────────────────────

function PolicyList({
  query,
  canEdit,
  onSelect,
}: {
  query: ReturnType<typeof usePolicies>
  canEdit: boolean
  onSelect: (p: AbacPolicy) => void
}) {
  const rows = query.data ?? []

  const columns = useMemo(
    () => [
      {
        header: 'Effect',
        sortKey: 'effect',
        sortAccessor: (p: AbacPolicy) => p.effect,
        accessor: (p: AbacPolicy) => (
          <span
            className={cn(
              'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold',
              p.effect === 'deny' ? 'bg-red-100 text-red-700' : 'bg-emerald-100 text-emerald-700',
            )}
          >
            {p.effect === 'deny' ? <ShieldX className="h-3 w-3" /> : <ShieldCheck className="h-3 w-3" />}
            {p.effect}
          </span>
        ),
      },
      {
        header: 'Policy',
        sortKey: 'key',
        sortAccessor: (p: AbacPolicy) => p.key,
        accessor: (p: AbacPolicy) => (
          <div className="min-w-0">
            <div className="truncate font-medium text-gray-900">{p.key}</div>
            {p.description && (
              <div className="truncate text-xs text-gray-500">{p.description}</div>
            )}
          </div>
        ),
      },
      {
        header: 'Applies to',
        accessor: (p: AbacPolicy) => (
          <span className="font-mono text-xs text-gray-700">
            {p.resourceType}<span className="text-gray-400"> / </span>{p.action}
          </span>
        ),
        sortKey: 'resourceType',
        sortAccessor: (p: AbacPolicy) => `${p.resourceType}/${p.action}`,
      },
      {
        header: 'Conditions',
        accessor: (p: AbacPolicy) =>
          p.conditionCount > 0 ? (
            <span className="text-sm text-gray-700">{p.conditionCount}</span>
          ) : (
            // An unconditional PERMIT is worth flagging: it applies to everyone who clears the
            // coarse permission check, which is precisely the shape a reviewer should look at twice.
            <span
              className={cn(
                'inline-flex items-center gap-1 text-xs',
                p.effect === 'permit' ? 'text-amber-700' : 'text-gray-400',
              )}
              title={
                p.effect === 'permit'
                  ? 'Unconditional: applies to every caller who holds the permission.'
                  : 'Unconditional deny.'
              }
            >
              {p.effect === 'permit' && <AlertTriangle className="h-3 w-3" />}
              none
            </span>
          ),
        sortKey: 'conditionCount',
        sortAccessor: (p: AbacPolicy) => p.conditionCount,
      },
      {
        header: 'Owner',
        accessor: (p: AbacPolicy) =>
          p.isPlatformAuthored ? (
            <span
              className="inline-flex items-center gap-1 text-xs text-gray-600"
              title="Platform-authored: governs every tenant. A brand may tighten it, never relax it."
            >
              <Globe className="h-3 w-3" /> Platform
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 text-xs text-gray-600">
              <Building2 className="h-3 w-3" /> Brand
            </span>
          ),
        sortKey: 'owner',
        sortAccessor: (p: AbacPolicy) => (p.isPlatformAuthored ? 'platform' : 'brand'),
      },
      {
        header: 'Status',
        accessor: (p: AbacPolicy) => (
          <span className={cn('text-xs font-medium', p.isActive ? 'text-emerald-700' : 'text-gray-400')}>
            {p.isActive ? 'active' : 'inactive'}
          </span>
        ),
        sortKey: 'isActive',
        sortAccessor: (p: AbacPolicy) => (p.isActive ? 1 : 0),
      },
    ],
    [],
  )

  if (query.isLoading) return <LoadingState />
  if (query.isError) return <ErrorState error={query.error} />

  return (
    <FilterableTable<AbacPolicy>
      columns={columns}
      data={rows}
      keyFn={(p) => p.id}
      onRowClick={canEdit ? onSelect : undefined}
      unit="policy"
      searchPlaceholder="Search policies…"
      searchAccessor={(p) => `${p.key} ${p.description ?? ''} ${p.permissionCode ?? ''}`}
      filters={[
        {
          key: 'effect',
          allLabel: 'All effects',
          value: (p) => p.effect,
          options: [
            { value: 'permit', label: 'Permit' },
            { value: 'deny', label: 'Deny' },
          ],
        },
        {
          key: 'owner',
          allLabel: 'All owners',
          value: (p) => (p.isPlatformAuthored ? 'platform' : 'brand'),
          options: [
            { value: 'platform', label: 'Platform' },
            { value: 'brand', label: 'Brand' },
          ],
        },
      ]}
      initialSort={{ key: 'resourceType', dir: 'asc' }}
      emptyMessage="No policies yet. Run the ABAC migrations to seed the projection of the existing role permissions."
      noMatchMessage="No policies match those filters."
      csvExport={{
        filename: 'abac-policies.csv',
        columns: [
          { header: 'key', value: (p) => p.key },
          { header: 'effect', value: (p) => p.effect },
          { header: 'resource', value: (p) => p.resourceType },
          { header: 'action', value: (p) => p.action },
          { header: 'permission_code', value: (p) => p.permissionCode ?? '' },
          { header: 'conditions', value: (p) => String(p.conditionCount) },
          { header: 'owner', value: (p) => (p.isPlatformAuthored ? 'platform' : 'brand') },
          { header: 'active', value: (p) => String(p.isActive) },
        ],
      }}
    />
  )
}

// ── Explain feed ────────────────────────────────────────────────────────────────────────────────

function ExplainList({ query }: { query: ReturnType<typeof useDecisions> }) {
  if (query.isError && isForbiddenError(query.error)) return <ForbiddenState />
  if (query.isLoading) return <LoadingState />
  if (query.isError) return <ErrorState error={query.error} />

  const rows = query.data ?? []

  if (rows.length === 0) {
    return (
      <div className="rounded-lg border border-dashed border-gray-200 p-8 text-center">
        <p className="text-sm text-gray-600">No decisions recorded yet.</p>
        <p className="mt-1 text-xs text-gray-400">
          The engine logs a row for every decision once <code>Abac:Enabled</code> is on. Until then
          this feed stays empty.
        </p>
      </div>
    )
  }

  return (
    <div className="space-y-2">
      {rows.map((d, i) => (
        <DecisionRow key={`${d.occurredAt}-${i}`} decision={d} />
      ))}
    </div>
  )
}

function DecisionRow({ decision: d }: { decision: AbacDecision }) {
  const [open, setOpen] = useState(false)

  // The row that matters most during a shadow window: ABAC and the existing gate disagreed.
  const disagrees = d.rbacAllowed !== null && d.rbacAllowed !== (d.decision === 'permit')
  const looser = disagrees && d.decision === 'permit'

  return (
    <div
      className={cn(
        'rounded-lg border p-3 text-sm',
        looser ? 'border-red-300 bg-red-50' : disagrees ? 'border-amber-300 bg-amber-50' : 'border-gray-200',
      )}
    >
      <button onClick={() => setOpen((o) => !o)} className="flex w-full items-start gap-3 text-left">
        <span
          className={cn(
            'mt-0.5 rounded-full px-2 py-0.5 text-xs font-semibold',
            d.decision === 'permit' ? 'bg-emerald-100 text-emerald-700' : 'bg-red-100 text-red-700',
          )}
        >
          {d.decision}
        </span>

        <div className="min-w-0 flex-1">
          <div className="font-mono text-xs text-gray-700">
            {d.resourceType}<span className="text-gray-400"> / </span>{d.action}
          </div>
          <div className="truncate text-xs text-gray-600">{d.reason}</div>
        </div>

        <div className="shrink-0 text-right">
          {d.mode === 'shadow' && (
            <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-medium text-gray-500">
              shadow · not enforced
            </span>
          )}
          <div className="mt-0.5 flex items-center justify-end gap-1 text-[11px] text-gray-400">
            <Clock className="h-3 w-3" />
            {new Date(d.occurredAt).toLocaleString()}
          </div>
        </div>
      </button>

      {disagrees && (
        <p className={cn('mt-2 text-xs font-medium', looser ? 'text-red-800' : 'text-amber-800')}>
          {looser
            ? 'ABAC would PERMIT what the permission gate denies — this widens access and must be fixed before cutover.'
            : 'ABAC would DENY what the permission gate allows — review before cutting this module over.'}
        </p>
      )}

      {open && (
        <dl className="mt-3 space-y-1 border-t border-gray-200 pt-3 text-xs">
          <Row label="Matched policy" value={d.matchedPolicy ?? '(none matched — default deny)'} />
          <Row label="Subject" value={d.userId ?? d.customerId ?? '(anonymous)'} />
          <Row label="Token lane" value={d.tokenUse ?? '—'} />
          <Row label="Resource id" value={d.resourceId ?? '—'} />
          <Row
            label="Permission gate"
            value={d.rbacAllowed === null ? 'not comparable' : d.rbacAllowed ? 'allowed' : 'denied'}
          />
          <Row label="Latency" value={d.latencyMicroseconds != null ? `${d.latencyMicroseconds} µs` : '—'} />
          {d.attributes && (
            <div className="pt-1">
              <dt className="text-gray-500">Attributes compared</dt>
              <dd>
                <pre className="mt-1 max-h-56 overflow-auto rounded bg-gray-900 p-2 text-[11px] leading-relaxed text-gray-100">
                  {safeFormat(d.attributes)}
                </pre>
              </dd>
            </div>
          )}
        </dl>
      )}
    </div>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex gap-2">
      <dt className="w-32 shrink-0 text-gray-500">{label}</dt>
      <dd className="min-w-0 flex-1 break-all font-mono text-gray-800">{value}</dd>
    </div>
  )
}

function safeFormat(json: string): string {
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

// ── Edit drawer ─────────────────────────────────────────────────────────────────────────────────

function PolicyDrawer({
  policy,
  canEdit,
  submitting,
  onClose,
  onSave,
}: {
  policy: AbacPolicy | null
  canEdit: boolean
  submitting: boolean
  onClose: () => void
  onSave: (payload: { isActive?: boolean; priority?: number }) => Promise<void>
}) {
  const [isActive, setIsActive] = useState(true)
  const [priority, setPriority] = useState(100)

  // Reset the form whenever a different policy is opened.
  const key = policy?.id ?? ''
  const [lastKey, setLastKey] = useState('')
  if (key !== lastKey) {
    setLastKey(key)
    if (policy) {
      setIsActive(policy.isActive)
      setPriority(policy.priority)
    }
  }

  if (!policy) return null

  // A brand cannot deactivate a platform policy — the server refuses it, so the control is
  // disabled rather than offered and then rejected.
  const canDeactivate = canEdit && !policy.isPlatformAuthored

  return (
    <FormDrawer
      open
      onClose={onClose}
      title={policy.key}
      eyebrow={`${policy.resourceType} / ${policy.action}`}
      icon={policy.effect === 'deny' ? ShieldX : ShieldCheck}
      submitting={submitting}
      submitDisabled={!canEdit}
      onSubmit={() => onSave({ isActive, priority })}
    >
      <div className="space-y-4">
        {policy.description && <p className="text-sm text-gray-600">{policy.description}</p>}

        {policy.isPlatformAuthored && (
          <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-xs text-blue-900">
            <strong className="font-semibold">Platform-authored.</strong> This rule governs every
            tenant. A brand may tighten it — by raising its priority or adding its own stricter
            policy — but cannot deactivate or expire it.
          </div>
        )}

        {policy.conditionCount === 0 && policy.effect === 'permit' && (
          <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
            <strong className="font-semibold">Unconditional permit.</strong> It applies to every
            caller who clears the coarse permission check, with no attribute test at all.
          </div>
        )}

        <dl className="space-y-1 text-xs">
          <Row label="Effect" value={policy.effect} />
          <Row label="Permission" value={policy.permissionCode ?? '—'} />
          <Row label="Conditions" value={String(policy.conditionCount)} />
          <Row label="Effective from" value={new Date(policy.effectiveFrom).toLocaleString()} />
          <Row
            label="Effective to"
            value={policy.effectiveTo ? new Date(policy.effectiveTo).toLocaleString() : 'open-ended'}
          />
        </dl>

        <label className="block">
          <span className="text-xs font-medium text-gray-700">
            Priority <span className="text-gray-400">(lower runs first)</span>
          </span>
          <input
            type="number"
            value={priority}
            disabled={!canEdit}
            onChange={(e) => setPriority(Number(e.target.value))}
            className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm disabled:bg-gray-50"
          />
          <span className="mt-1 block text-[11px] text-gray-400">
            Ordering applies within an effect. A deny always beats a permit regardless of priority.
          </span>
        </label>

        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={isActive}
            disabled={!canDeactivate}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300"
          />
          <span className={cn('text-sm', canDeactivate ? 'text-gray-700' : 'text-gray-400')}>
            Active
          </span>
        </label>
      </div>
    </FormDrawer>
  )
}
