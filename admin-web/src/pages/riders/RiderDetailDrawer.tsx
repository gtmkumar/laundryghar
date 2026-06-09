import { useState } from 'react'
import { Bike, Loader2, Check, Ban, Mail, Phone, Star, Pencil } from 'lucide-react'
import { useRider, useVerifyRider, useRejectRider } from '@/hooks/useRiders'
import { usePermissions } from '@/hooks/usePermissions'
import { FormDrawer, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
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

  const footerNode =
    rider && (canManage || showKycActions) ? (
      rejecting ? (
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
      )
    ) : undefined

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Bike}
      eyebrow="Rider profile"
      title={rider?.riderName ?? rider?.email ?? rider?.riderCode ?? 'Rider'}
      width="md"
      error={error}
      footer={footerNode}
    >
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
          <DetailSection title="Identity">
            <DetailRow label="Name" value={rider.riderName ?? '—'} />
            <DetailRow
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
            <DetailRow
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
            <DetailRow
              label="Account status"
              value={<span className="capitalize">{rider.userStatus ? humanise(rider.userStatus) : '—'}</span>}
            />
          </DetailSection>

          {/* Assignment */}
          <DetailSection title="Assignment">
            <DetailRow label="Franchise" value={rider.franchiseName ?? '—'} />
            <DetailRow label="Primary store" value={rider.primaryStoreName ?? '—'} />
            <DetailRow
              label="Employment"
              value={EMPLOYMENT_LABEL[rider.employmentType] ?? humanise(rider.employmentType)}
            />
          </DetailSection>

          {/* Vehicle & documents */}
          <DetailSection title="Vehicle &amp; documents">
            <DetailRow label="Vehicle" value={VEHICLE_LABEL[rider.vehicleType] ?? humanise(rider.vehicleType)} />
            <DetailRow label="Vehicle number" value={rider.vehicleNumber ?? '—'} />
            <DetailRow label="Vehicle model" value={rider.vehicleModel ?? '—'} />
            <DetailRow label="Driving licence" value={rider.drivingLicenseNumber ?? '—'} />
            <DetailRow label="DL expiry" value={formatDate(rider.dlExpiryDate)} />
            <DetailRow label="Insurance expiry" value={formatDate(rider.insuranceExpiryDate)} />
          </DetailSection>

          {/* Capacity */}
          <DetailSection title="Capacity &amp; service">
            <DetailRow label="Daily pickups" value={rider.dailyPickupCapacity} />
            <DetailRow label="Daily deliveries" value={rider.dailyDeliveryCapacity} />
            <DetailRow label="Service radius" value={`${rider.serviceRadiusKm} km`} />
            <DetailRow label="Current load" value={rider.currentLoad} />
            <DetailRow
              label="Availability"
              value={
                <span className="space-x-2">
                  <Pill on={rider.isOnline}>{rider.isOnline ? 'Online' : 'Offline'}</Pill>
                  <Pill on={rider.isOnDuty}>{rider.isOnDuty ? 'On duty' : 'Off duty'}</Pill>
                </span>
              }
            />
          </DetailSection>

          {/* Performance */}
          <DetailSection title="Performance">
            <DetailRow
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
            <DetailRow
              label="Completion rate"
              value={rider.completionRate != null ? `${Math.round(rider.completionRate * 100)}%` : '—'}
            />
            <DetailRow label="Lifetime deliveries" value={rider.lifetimeDeliveries} />
          </DetailSection>

          {/* Meta */}
          <DetailSection title="Record">
            <DetailRow label="Joined" value={formatDate(rider.createdAt)} />
            <DetailRow label="Last updated" value={formatDate(rider.updatedAt)} />
          </DetailSection>
        </div>
      )}
    </FormDrawer>
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
