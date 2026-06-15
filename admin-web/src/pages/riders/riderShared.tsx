import { cn } from '@/lib/utils'

// Shared label maps + badges for the Riders feature, so the list, detail drawer
// and edit drawer stay visually consistent without duplicating Tailwind classes.

// This module pairs the rider badge components with their small co-located label
// maps + pure helpers. Non-component exports are disabled individually for
// react-refresh rather than split into a separate module.
// eslint-disable-next-line react-refresh/only-export-components
export const VEHICLE_LABEL: Record<string, string> = {
  two_wheeler: 'Two-wheeler',
  three_wheeler: 'Three-wheeler',
  four_wheeler: 'Four-wheeler',
  cycle: 'Cycle',
  foot: 'On foot',
}

// eslint-disable-next-line react-refresh/only-export-components
export const EMPLOYMENT_LABEL: Record<string, string> = {
  employee: 'Employee',
  contractor: 'Contractor',
  gig: 'Gig',
  outsourced: 'Outsourced',
}

const KYC_BADGE: Record<string, string> = {
  verified: 'bg-emerald-100 text-emerald-700',
  submitted: 'bg-sky-100 text-sky-700',
  pending: 'bg-amber-100 text-amber-700',
  rejected: 'bg-rose-100 text-rose-700',
  expired: 'bg-orange-100 text-orange-700',
  not_submitted: 'bg-slate-100 text-slate-600',
}

const VEHICLE_BADGE: Record<string, string> = {
  approved: 'bg-emerald-100 text-emerald-700',
  under_review: 'bg-sky-100 text-sky-700',
  pending: 'bg-amber-100 text-amber-700',
  rejected: 'bg-rose-100 text-rose-700',
}

const DOC_BADGE: Record<string, string> = {
  approved: 'bg-emerald-100 text-emerald-700',
  pending: 'bg-amber-100 text-amber-700',
  rejected: 'bg-rose-100 text-rose-700',
}

/** Human labels for the KYC document types the rider app uploads. */
// eslint-disable-next-line react-refresh/only-export-components
export const DOC_TYPE_LABEL: Record<string, string> = {
  license: 'Driving licence',
  rc: 'Registration certificate',
  insurance: 'Insurance',
  id: 'ID proof',
  photo: 'Rider photo',
}

const STATUS_DOT: Record<string, { dot: string; text: string }> = {
  active: { dot: 'bg-emerald-500', text: 'text-emerald-700' },
  suspended: { dot: 'bg-rose-500', text: 'text-rose-700' },
  terminated: { dot: 'bg-gray-400', text: 'text-gray-500' },
  inactive: { dot: 'bg-gray-300', text: 'text-gray-500' },
}

// eslint-disable-next-line react-refresh/only-export-components
export function humanise(value: string): string {
  return value.replace(/_/g, ' ')
}

// eslint-disable-next-line react-refresh/only-export-components
export function formatDate(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

/** A rider is awaiting a KYC decision when pending or submitted. */
// eslint-disable-next-line react-refresh/only-export-components
export function isKycActionable(kycStatus: string): boolean {
  return kycStatus === 'pending' || kycStatus === 'submitted'
}

/** The rider's vehicle still needs a reviewer decision (not yet approved/rejected). */
// eslint-disable-next-line react-refresh/only-export-components
export function isVehicleActionable(status: string): boolean {
  return status === 'pending' || status === 'under_review'
}

/**
 * Whether a rider belongs in the verification queue: KYC awaiting a decision OR
 * vehicle awaiting one. Mirrors the backend's queue predicate so the client-side
 * default filter and the server stay consistent.
 */
// eslint-disable-next-line react-refresh/only-export-components
export function needsVerification(kycStatus: string, vehicleStatus: string): boolean {
  return isKycActionable(kycStatus) || isVehicleActionable(vehicleStatus)
}

export function KycBadge({ status }: { status: string }) {
  return (
    <span
      className={cn(
        'inline-block rounded-full px-2.5 py-1 text-xs font-medium capitalize',
        KYC_BADGE[status] ?? 'bg-slate-100 text-slate-600',
      )}
    >
      {humanise(status)}
    </span>
  )
}

export function VehicleBadge({ status }: { status: string }) {
  return (
    <span
      className={cn(
        'inline-block rounded-full px-2.5 py-1 text-xs font-medium capitalize',
        VEHICLE_BADGE[status] ?? 'bg-slate-100 text-slate-600',
      )}
    >
      {humanise(status)}
    </span>
  )
}

export function DocStatusBadge({ status }: { status: string }) {
  return (
    <span
      className={cn(
        'inline-block rounded-full px-2 py-0.5 text-xs font-medium capitalize',
        DOC_BADGE[status] ?? 'bg-slate-100 text-slate-600',
      )}
    >
      {humanise(status)}
    </span>
  )
}

export function StatusBadge({ status }: { status: string }) {
  const s = STATUS_DOT[status] ?? STATUS_DOT.inactive
  return (
    <span className="inline-flex items-center gap-1.5 text-xs font-medium capitalize">
      <span className={cn('h-1.5 w-1.5 rounded-full', s.dot)} />
      <span className={s.text}>{humanise(status)}</span>
    </span>
  )
}
