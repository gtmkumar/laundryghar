import { Badge } from '@/components/ui/badge'

/** Pickup-request lifecycle statuses surfaced on the admin queue. */
export const PICKUP_STATUSES = [
  'pending',
  'assigned',
  'in_progress',
  'completed',
  'cancelled',
] as const

/** Payment intents recorded at booking time (PickupRequestDto.paymentPreference). */
export const PAYMENT_PREFERENCES: { value: string; label: string }[] = [
  { value: 'wallet', label: 'Wallet' },
  { value: 'cod', label: 'Cash on delivery' },
  { value: 'upi-deferred', label: 'UPI / Card' },
]

export function paymentPreferenceLabel(pref: string): string {
  return PAYMENT_PREFERENCES.find((p) => p.value === pref)?.label ?? pref
}

export function humanise(s: string): string {
  return s.replace(/[-_]/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())
}

const PICKUP_STATUS_VARIANT: Record<
  string,
  'default' | 'secondary' | 'success' | 'warning' | 'destructive'
> = {
  pending: 'warning',
  assigned: 'default',
  in_progress: 'default',
  completed: 'success',
  cancelled: 'destructive',
}

export function PickupStatusBadge({ status }: { status: string }) {
  return (
    <Badge variant={PICKUP_STATUS_VARIANT[status] ?? 'secondary'} className="capitalize">
      {status.replace(/_/g, ' ')}
    </Badge>
  )
}

export function PaymentPrefBadge({ pref }: { pref: string }) {
  return (
    <Badge variant={pref === 'cod' ? 'warning' : 'secondary'}>
      {paymentPreferenceLabel(pref)}
    </Badge>
  )
}

/** "10:00:00" → "10:00". Tolerates already-short values. */
export function formatTime(t: string): string {
  return t.length >= 5 ? t.slice(0, 5) : t
}

/** "10:00:00" + "12:00:00" → "10:00 – 12:00". */
export function formatWindow(start: string, end: string): string {
  return `${formatTime(start)} – ${formatTime(end)}`
}
