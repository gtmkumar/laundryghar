import { useState } from 'react'
import {
  X,
  Bike,
  Loader2,
  AlertTriangle,
  Check,
  Ban,
  Mail,
  Phone,
  Star,
  Pencil,
} from 'lucide-react'
import { useRider, useVerifyRider, useRejectRider } from '@/hooks/useRiders'
import { usePermissions } from '@/hooks/usePermissions'
import type { RiderDto } from '@/types/api'
import {
  VEHICLE_LABEL,
  EMPLOYMENT_LABEL,
  KycBadge,
  StatusBadge,
  formatDate,
  humanise,
  isKycActionable,
} from './riderShared'

interface Props {
  riderId: string | null
  open: boolean
  onClose: () => void
  onEdit?: (rider: RiderDto) => void
}

export function RiderDetailDrawer({ riderId, open, onClose, onEdit }: Props) {
  const { hasPermission } = usePermissions()
  const { data: rider, isLoading, isError } = useRider(open ? riderId : null)
  const verify = useVerifyRider()
  const reject = useRejectRider()

  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  if (!open) return null

  const canManage = hasPermission('rider.manage')
  const canVerify = hasPermission('rider.verify')
  const showKycActions = !!rider && canVerify && isKycActionable(rider.kycStatus)
  const busy = verify.isPending || reject.isPending

  const onApprove = async () => {
    if (!rider) return
    setError(null)
    try {
      await verify.mutateAsync(rider.id)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not approve KYC.')
    }
  }

  const onReject = async () => {
    if (!rider) return
    setError(null)
    try {
      await reject.mutateAsync({ id: rider.id, reason: reason.trim() || undefined })
      setRejecting(false)
      setReason('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not reject KYC.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/30" onClick={onClose}>
      <div
        className="flex h-full w-full max-w-lg flex-col bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
          <div className="flex items-center gap-2.5">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
              <Bike className="h-4 w-4" />
            </span>
            <div>
              <p className="text-xs font-medium text-gray-400">Rider profile</p>
              <h2 className="text-xl font-bold text-gray-900">
                {rider?.riderName ?? rider?.email ?? rider?.riderCode ?? 'Rider'}
              </h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-5">
          {isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading rider…
            </div>
          ) : isError || !rider ? (
            <div className="py-24 text-center text-sm text-red-600">Couldn’t load this rider.</div>
          ) : (
            <div className="space-y-6">
              {/* Badges */}
              <div className="flex flex-wrap items-center gap-3">
                <KycBadge status={rider.kycStatus} />
                <StatusBadge status={rider.status} />
                <span className="rounded-full bg-gray-100 px-2.5 py-1 font-mono text-xs text-gray-600">
                  {rider.riderCode}
                </span>
              </div>

              {/* Identity */}
              <Section title="Identity">
                <Row label="Name" value={rider.riderName ?? '—'} />
                <Row
                  label="Email"
                  value={
                    rider.email ? (
                      <span className="inline-flex items-center gap-1.5">
                        <Mail className="h-3.5 w-3.5 text-gray-400" /> {rider.email}
                      </span>
                    ) : (
                      '—'
                    )
                  }
                />
                <Row
                  label="Phone"
                  value={
                    rider.phone ? (
                      <span className="inline-flex items-center gap-1.5">
                        <Phone className="h-3.5 w-3.5 text-gray-400" /> {rider.phone}
                      </span>
                    ) : (
                      '—'
                    )
                  }
                />
                <Row
                  label="Account status"
                  value={<span className="capitalize">{rider.userStatus ? humanise(rider.userStatus) : '—'}</span>}
                />
              </Section>

              {/* Assignment */}
              <Section title="Assignment">
                <Row label="Franchise" value={rider.franchiseName ?? '—'} />
                <Row label="Primary store" value={rider.primaryStoreName ?? '—'} />
                <Row
                  label="Employment"
                  value={EMPLOYMENT_LABEL[rider.employmentType] ?? humanise(rider.employmentType)}
                />
              </Section>

              {/* Vehicle & documents */}
              <Section title="Vehicle &amp; documents">
                <Row label="Vehicle" value={VEHICLE_LABEL[rider.vehicleType] ?? humanise(rider.vehicleType)} />
                <Row label="Vehicle number" value={rider.vehicleNumber ?? '—'} />
                <Row label="Vehicle model" value={rider.vehicleModel ?? '—'} />
                <Row label="Driving licence" value={rider.drivingLicenseNumber ?? '—'} />
                <Row label="DL expiry" value={formatDate(rider.dlExpiryDate)} />
                <Row label="Insurance expiry" value={formatDate(rider.insuranceExpiryDate)} />
              </Section>

              {/* Capacity */}
              <Section title="Capacity &amp; service">
                <Row label="Daily pickups" value={rider.dailyPickupCapacity} />
                <Row label="Daily deliveries" value={rider.dailyDeliveryCapacity} />
                <Row label="Service radius" value={`${rider.serviceRadiusKm} km`} />
                <Row label="Current load" value={rider.currentLoad} />
                <Row
                  label="Availability"
                  value={
                    <span className="space-x-2">
                      <Pill on={rider.isOnline}>{rider.isOnline ? 'Online' : 'Offline'}</Pill>
                      <Pill on={rider.isOnDuty}>{rider.isOnDuty ? 'On duty' : 'Off duty'}</Pill>
                    </span>
                  }
                />
              </Section>

              {/* Performance */}
              <Section title="Performance">
                <Row
                  label="Rating"
                  value={
                    rider.ratingAverage != null ? (
                      <span className="inline-flex items-center gap-1">
                        <Star className="h-3.5 w-3.5 fill-amber-400 text-amber-400" />
                        {rider.ratingAverage.toFixed(1)}
                        <span className="text-gray-400">({rider.ratingCount})</span>
                      </span>
                    ) : (
                      '—'
                    )
                  }
                />
                <Row
                  label="Completion rate"
                  value={rider.completionRate != null ? `${Math.round(rider.completionRate * 100)}%` : '—'}
                />
                <Row label="Lifetime deliveries" value={rider.lifetimeDeliveries} />
              </Section>

              {/* Meta */}
              <Section title="Record">
                <Row label="Joined" value={formatDate(rider.createdAt)} />
                <Row label="Last updated" value={formatDate(rider.updatedAt)} />
              </Section>

              {error && (
                <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">
                  <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                  <span>{error}</span>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        {rider && (canManage || showKycActions) && (
          <div className="border-t border-gray-100 px-6 py-4">
            {rejecting ? (
              <div className="space-y-2">
                <input
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
                  placeholder="Reason for rejection (optional)"
                  autoFocus
                />
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      setRejecting(false)
                      setReason('')
                    }}
                    className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={onReject}
                    disabled={busy}
                    className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-4 py-2 text-sm font-semibold text-white hover:bg-rose-700 disabled:opacity-60"
                  >
                    {reject.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Ban className="h-3.5 w-3.5" />}
                    Confirm reject
                  </button>
                </div>
              </div>
            ) : (
              <div className="flex justify-end gap-2">
                {canManage && onEdit && (
                  <button
                    type="button"
                    onClick={() => onEdit(rider)}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
                  >
                    <Pencil className="h-3.5 w-3.5" /> Edit
                  </button>
                )}
                {showKycActions && (
                  <>
                    <button
                      type="button"
                      onClick={() => setRejecting(true)}
                      disabled={busy}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-rose-200 px-4 py-2 text-sm font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-60"
                    >
                      <Ban className="h-3.5 w-3.5" /> Reject
                    </button>
                    <button
                      type="button"
                      onClick={onApprove}
                      disabled={busy}
                      className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
                    >
                      {verify.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
                      Approve
                    </button>
                  </>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-900">{title}</h3>
      <dl className="divide-y divide-gray-50 rounded-xl border border-gray-100">{children}</dl>
    </section>
  )
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
      <dt className="text-gray-500">{label}</dt>
      <dd className="text-right font-medium text-gray-900">{value}</dd>
    </div>
  )
}

function Pill({ on, children }: { on: boolean; children: React.ReactNode }) {
  return (
    <span
      className={
        on
          ? 'inline-block rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-700'
          : 'inline-block rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-500'
      }
    >
      {children}
    </span>
  )
}
