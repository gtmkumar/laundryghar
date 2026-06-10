import { useEffect, useMemo, useState } from 'react'
import {
  Truck,
  Loader2,
  Search,
  Bike,
  UserCheck,
  AlertTriangle,
  Check,
  XCircle,
} from 'lucide-react'
import { FormDrawer, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
import { usePermissions } from '@/hooks/usePermissions'
import { usePickupRequest, useAssignPickup, useRejectPickup } from '@/hooks/usePickups'
import { useRidersInfinite } from '@/hooks/useRiders'
import { formatCurrency, formatDate, formatDateTime, cn } from '@/lib/utils'
import type { PickupRequestDto, RiderDto } from '@/types/api'
import { PickupStatusBadge, PaymentPrefBadge, formatWindow } from './pickupShared'

// Backend gate: POST /pickup-requests/{id}/assign   → permission:pickup.assign.
// Backend gate: POST /pickup-requests/{id}/reject   → permission:pickup.assign (same role).
const PERM_ASSIGN = 'pickup.assign'

const REJECTION_REASON_MAX = 300

// Only pending requests can be handed to a rider or rejected; once assigned the leg is live.
const ASSIGNABLE_STATUSES = new Set(['pending'])

interface Props {
  pickupId: string | null
  open: boolean
  onClose: () => void
}

export function PickupDetailDrawer({ pickupId, open, onClose }: Props) {
  const { hasPermission } = usePermissions()
  const canAssign = hasPermission(PERM_ASSIGN)

  const { data: pickup, isLoading, isError, error } = usePickupRequest(open ? pickupId : null)

  const [assigning, setAssigning] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  // Reset all panels whenever the drawer target changes.
  useEffect(() => {
    setAssigning(false)
    setRejecting(false)
    setActionError(null)
  }, [pickupId, open])

  const isAssignable = !!pickup && ASSIGNABLE_STATUSES.has(pickup.status)
  const activePanel = assigning ? 'assign' : rejecting ? 'reject' : null

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      eyebrow="Pickup request"
      title={pickup?.requestNumber ?? 'Pickup request'}
      icon={Truck}
      width="lg"
      error={actionError}
      footer={
        canAssign && isAssignable && activePanel === null ? (
          <div className="flex items-center justify-end gap-2">
            <button
              type="button"
              onClick={() => { setRejecting(true); setActionError(null) }}
              className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 bg-white px-4 py-2 text-sm font-semibold text-red-600 hover:bg-red-50"
            >
              <XCircle className="h-4 w-4" /> Reject
            </button>
            <button
              type="button"
              onClick={() => { setAssigning(true); setActionError(null) }}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              <UserCheck className="h-4 w-4" /> Assign rider
            </button>
          </div>
        ) : null
      }
    >
      {isLoading && (
        <div className="flex items-center justify-center py-16 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading request…
        </div>
      )}
      {isError && (
        <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error instanceof Error ? error.message : 'Could not load this request.'}</span>
        </div>
      )}

      {pickup && (
        <>
          <DetailSection title="Summary">
            <DetailRow label="Status" value={<PickupStatusBadge status={pickup.status} />} />
            <DetailRow
              label="Customer"
              value={<span className="font-mono text-xs">{pickup.customerId}</span>}
            />
            <DetailRow
              label="Store"
              value={
                pickup.storeId ? (
                  <span className="font-mono text-xs">{pickup.storeId}</span>
                ) : (
                  <span className="text-gray-400">Unassigned</span>
                )
              }
            />
            <DetailRow label="Payment" value={<PaymentPrefBadge pref={pickup.paymentPreference} />} />
            <DetailRow
              label="Express"
              value={pickup.isExpress ? 'Yes' : <span className="text-gray-400">No</span>}
            />
            <DetailRow label="Created" value={formatDateTime(pickup.createdAt)} />
          </DetailSection>

          <DetailSection title="Pickup window">
            <DetailRow label="Date" value={formatDate(pickup.pickupDate)} />
            <DetailRow
              label="Window"
              value={formatWindow(pickup.pickupWindowStart, pickup.pickupWindowEnd)}
            />
          </DetailSection>

          <CartItemsSection pickup={pickup} />

          {/* Assign panel — opens in place of the footer actions. */}
          {assigning && (
            <AssignRiderPanel
              pickupId={pickup.id}
              onCancel={() => { setAssigning(false); setActionError(null) }}
              onError={setActionError}
              onAssigned={() => { setAssigning(false); onClose() }}
            />
          )}

          {/* Reject panel — destructive, requires a reason. */}
          {rejecting && (
            <RejectPanel
              pickupId={pickup.id}
              onCancel={() => { setRejecting(false); setActionError(null) }}
              onError={setActionError}
              onRejected={() => { setRejecting(false); onClose() }}
            />
          )}
        </>
      )}
    </FormDrawer>
  )
}

// ── Cart items (the customer's estimated booking lines) ─────────────────────────

function CartItemsSection({ pickup }: { pickup: PickupRequestDto }) {
  const items = pickup.cartItems ?? []
  const estimatedTotal =
    pickup.estimatedAmount ??
    items.reduce((sum, i) => sum + (i.estimatedUnitPrice ?? 0) * i.quantity, 0)

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-gray-900">Estimated items</h3>
        <span className="text-xs text-gray-400">
          {pickup.estimatedItems ?? items.reduce((s, i) => s + i.quantity, 0)} item
          {(pickup.estimatedItems ?? 0) === 1 ? '' : 's'}
        </span>
      </div>

      {items.length === 0 ? (
        <p className="rounded-xl border border-dashed border-gray-200 px-3 py-4 text-center text-sm text-gray-400">
          No itemised estimate — the customer booked without selecting items.
        </p>
      ) : (
        <div className="overflow-hidden rounded-xl border border-gray-100">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-100 bg-gray-50/60 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
                <th className="px-3 py-2">Item</th>
                <th className="px-3 py-2 text-right">Qty</th>
                <th className="px-3 py-2 text-right">Est. unit</th>
                <th className="px-3 py-2 text-right">Est. line</th>
              </tr>
            </thead>
            <tbody>
              {items.map((it, i) => (
                <tr key={i} className="border-b border-gray-50 last:border-0">
                  <td className="px-3 py-2 text-gray-700">{it.displayLabel}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600">{it.quantity}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-gray-600">
                    {it.estimatedUnitPrice != null ? formatCurrency(it.estimatedUnitPrice) : '—'}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums font-medium text-gray-800">
                    {it.estimatedUnitPrice != null
                      ? formatCurrency(it.estimatedUnitPrice * it.quantity)
                      : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-gray-100 bg-gray-50/60">
                <td className="px-3 py-2 font-medium text-gray-700" colSpan={3}>
                  Estimated total
                </td>
                <td className="px-3 py-2 text-right tabular-nums font-semibold text-gray-900">
                  {formatCurrency(estimatedTotal)}
                </td>
              </tr>
            </tfoot>
          </table>
        </div>
      )}
      <p className="text-xs text-gray-400">
        Estimates from the booking flow. The final order is created after weighing at the store.
      </p>
    </section>
  )
}

// ── Reject panel ─────────────────────────────────────────────────────────────────

function RejectPanel({
  pickupId,
  onCancel,
  onError,
  onRejected,
}: {
  pickupId: string
  onCancel: () => void
  onError: (msg: string | null) => void
  onRejected: () => void
}) {
  const [reason, setReason] = useState('')
  const reject = useRejectPickup()

  const charsLeft = REJECTION_REASON_MAX - reason.length
  const canSubmit = reason.trim().length > 0 && charsLeft >= 0 && !reject.isPending

  const confirm = async () => {
    if (!canSubmit) return
    onError(null)
    try {
      await reject.mutateAsync({ id: pickupId, payload: { reason: reason.trim() } })
      onRejected()
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not reject this request.')
    }
  }

  return (
    <section className="space-y-3 rounded-xl border border-red-200 bg-red-50/50 p-3">
      <div className="flex items-center justify-between">
        <h3 className="flex items-center gap-1.5 text-sm font-semibold text-red-800">
          <XCircle className="h-4 w-4" /> Reject pickup request
        </h3>
        <button
          type="button"
          onClick={onCancel}
          className="text-xs font-medium text-gray-400 hover:text-gray-600"
        >
          Cancel
        </button>
      </div>

      <p className="text-xs text-red-700">
        This will cancel the pickup and release the slot. The customer will be notified. This action cannot be undone.
      </p>

      <div className="space-y-1">
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={3}
          maxLength={REJECTION_REASON_MAX}
          placeholder="Reason for rejection (required)…"
          className={cn(
            'w-full resize-none rounded-lg border bg-white px-3 py-2 text-sm outline-none',
            'focus:ring-2',
            charsLeft < 0
              ? 'border-red-400 focus:border-red-500 focus:ring-red-200'
              : 'border-gray-200 focus:border-red-400 focus:ring-red-100',
          )}
        />
        <p className={cn('text-right text-xs', charsLeft < 20 ? 'text-red-600' : 'text-gray-400')}>
          {charsLeft} remaining
        </p>
      </div>

      <div className="flex justify-end">
        <button
          type="button"
          onClick={confirm}
          disabled={!canSubmit}
          className="inline-flex items-center gap-1.5 rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-60"
        >
          {reject.isPending ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <XCircle className="h-3.5 w-3.5" />
          )}
          {reject.isPending ? 'Rejecting…' : 'Confirm rejection'}
        </button>
      </div>
    </section>
  )
}

// ── Assign-rider picker (server-side search over GET /riders) ────────────────────

function AssignRiderPanel({
  pickupId,
  onCancel,
  onError,
  onAssigned,
}: {
  pickupId: string
  onCancel: () => void
  onError: (msg: string | null) => void
  onAssigned: () => void
}) {
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<RiderDto | null>(null)

  // Debounce the search box → server query (matches RidersPage cadence).
  useEffect(() => {
    const t = setTimeout(() => setSearch(searchInput.trim()), 350)
    return () => clearTimeout(t)
  }, [searchInput])

  // Only active, KYC-verified riders should be assignable.
  const ridersQ = useRidersInfinite({
    search: search || undefined,
    status: 'active',
    kycStatus: 'verified',
    sort: 'name',
  })

  const riders = useMemo(
    () => ridersQ.data?.pages.flatMap((p) => p.list) ?? [],
    [ridersQ.data],
  )

  const assign = useAssignPickup()

  const confirm = async () => {
    if (!selected) return
    onError(null)
    try {
      await assign.mutateAsync({ id: pickupId, payload: { riderId: selected.id } })
      onAssigned()
    } catch (e) {
      onError(e instanceof Error ? e.message : 'Could not assign this rider.')
    }
  }

  return (
    <section className="space-y-3 rounded-xl border border-lg-green/30 bg-lg-green/[0.04] p-3">
      <div className="flex items-center justify-between">
        <h3 className="flex items-center gap-1.5 text-sm font-semibold text-gray-900">
          <UserCheck className="h-4 w-4 text-lg-green" /> Assign a rider
        </h3>
        <button
          type="button"
          onClick={onCancel}
          className="text-xs font-medium text-gray-400 hover:text-gray-600"
        >
          Cancel
        </button>
      </div>

      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
        <input
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Search rider name, code, phone…"
          className="w-full rounded-lg border border-gray-200 bg-white py-2 pl-9 pr-3 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
        />
      </div>

      <div className="max-h-56 overflow-y-auto rounded-lg border border-gray-100 bg-white">
        {ridersQ.isLoading ? (
          <div className="flex items-center justify-center py-8 text-gray-400">
            <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading riders…
          </div>
        ) : ridersQ.isError ? (
          <p className="px-3 py-6 text-center text-sm text-red-600">
            Couldn’t load riders. The roster service may be unavailable.
          </p>
        ) : riders.length === 0 ? (
          <p className="px-3 py-6 text-center text-sm text-gray-400">
            No active, verified riders match this search.
          </p>
        ) : (
          <ul className="divide-y divide-gray-50">
            {riders.map((r) => {
              const name = r.riderName ?? r.email ?? r.riderCode
              const sub = r.phone ?? r.email ?? r.riderCode
              const active = selected?.id === r.id
              return (
                <li key={r.id}>
                  <button
                    type="button"
                    onClick={() => setSelected(r)}
                    className={cn(
                      'flex w-full items-center gap-3 px-3 py-2 text-left transition-colors hover:bg-gray-50',
                      active && 'bg-lg-green/[0.06]',
                    )}
                  >
                    <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-lg-green/12 text-lg-green">
                      <Bike className="h-4 w-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium text-gray-900">{name}</span>
                      <span className="block truncate text-xs text-gray-400">
                        {sub}
                        {r.franchiseName ? ` · ${r.franchiseName}` : ''}
                        {` · load ${r.currentLoad}`}
                      </span>
                    </span>
                    {active && <Check className="h-4 w-4 shrink-0 text-lg-green" />}
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      <div className="flex items-center justify-between gap-2">
        <p className="min-w-0 truncate text-xs text-gray-400">
          {selected
            ? `Selected: ${selected.riderName ?? selected.riderCode}`
            : 'Pick a rider to continue.'}
        </p>
        <button
          type="button"
          onClick={confirm}
          disabled={!selected || assign.isPending}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
        >
          {assign.isPending ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <UserCheck className="h-3.5 w-3.5" />
          )}
          {assign.isPending ? 'Assigning…' : 'Confirm assignment'}
        </button>
      </div>
    </section>
  )
}
