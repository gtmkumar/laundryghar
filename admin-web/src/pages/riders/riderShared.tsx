import { cn } from '@/lib/utils'

// Shared label maps + badges for the Riders feature, so the list, detail drawer
// and edit drawer stay visually consistent without duplicating Tailwind classes.

export const VEHICLE_LABEL: Record<string, string> = {
  two_wheeler: 'Two-wheeler',
  three_wheeler: 'Three-wheeler',
  four_wheeler: 'Four-wheeler',
  cycle: 'Cycle',
  foot: 'On foot',
}

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

const STATUS_DOT: Record<string, { dot: string; text: string }> = {
  active: { dot: 'bg-emerald-500', text: 'text-emerald-700' },
  suspended: { dot: 'bg-rose-500', text: 'text-rose-700' },
  terminated: { dot: 'bg-gray-400', text: 'text-gray-500' },
  inactive: { dot: 'bg-gray-300', text: 'text-gray-500' },
}

export function humanise(value: string): string {
  return value.replace(/_/g, ' ')
}

export function formatDate(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

/** A rider is awaiting a KYC decision when pending or submitted. */
export function isKycActionable(kycStatus: string): boolean {
  return kycStatus === 'pending' || kycStatus === 'submitted'
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

export function StatusBadge({ status }: { status: string }) {
  const s = STATUS_DOT[status] ?? STATUS_DOT.inactive
  return (
    <span className="inline-flex items-center gap-1.5 text-xs font-medium capitalize">
      <span className={cn('h-1.5 w-1.5 rounded-full', s.dot)} />
      <span className={s.text}>{humanise(status)}</span>
    </span>
  )
}
