/**
 * Shared order presentation helpers — used by the Orders page cards/table, the
 * dashboard live feed, and the dashboard ops panels so every surface formats
 * statuses, ages, and chips identically.
 *
 * Status labels are i18n-wired (orders.status.*) with a Title-Case fallback so
 * an untranslated status still renders cleanly.
 */
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'
import { statusLabel, statusLabelKey } from './orderStatus'

// ── Status label (i18n + fallback) ─────────────────────────────────────────────

/**
 * Resolve a status label outside React (e.g. inside toasts/Web-Audio handlers).
 * Falls back to the Title-Case formatter when no translation exists.
 */
export function formatStatusLabel(status: string): string {
  const key = statusLabelKey(status)
  const translated = i18n.t(key)
  // i18next returns the key itself when missing → fall back to Title Case.
  return translated === key ? statusLabel(status) : translated
}

/** Hook variant so components re-render on language change. */
export function useStatusLabel(): (status: string) => string {
  const { t } = useTranslation()
  return (status: string) => {
    const key = statusLabelKey(status)
    const translated = t(key)
    return translated === key ? statusLabel(status) : translated
  }
}

// ── Relative duration ("2h 14m") ────────────────────────────────────────────────

/**
 * Compact human duration from a minute count: 134 → "2h 14m", 45 → "45m",
 * 2880 → "2d 0h". Always non-negative.
 */
export function formatDurationMinutes(totalMinutes: number): string {
  const mins = Math.max(0, Math.round(totalMinutes))
  if (mins < 60) return `${mins}m`
  const hours = Math.floor(mins / 60)
  const remMin = mins % 60
  if (hours < 24) return `${hours}h ${remMin}m`
  const days = Math.floor(hours / 24)
  const remHr = hours % 24
  return `${days}d ${remHr}h`
}

/** Minutes elapsed since an ISO timestamp (clamped at 0). */
export function minutesSince(iso: string): number {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return 0
  return Math.max(0, (Date.now() - then) / 60_000)
}

/** Minutes until an ISO timestamp; negative when in the past. */
export function minutesUntil(iso: string): number {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return 0
  return (then - Date.now()) / 60_000
}

// ── Age urgency (amber < 2h, red ≥ 2h) ──────────────────────────────────────────

export type AgeUrgency = 'amber' | 'red'

export function ageUrgency(minutes: number): AgeUrgency {
  return minutes >= 120 ? 'red' : 'amber'
}

export const AGE_URGENCY_CLASS: Record<AgeUrgency, string> = {
  amber: 'bg-amber-50 text-amber-700 border-amber-200',
  red: 'bg-red-50 text-red-700 border-red-200',
}

// ── Payment status → chip variant ───────────────────────────────────────────────

export type PaymentTone = 'paid' | 'due' | 'partial' | 'muted'

export function paymentTone(paymentStatus: string): PaymentTone {
  switch (paymentStatus) {
    case 'paid':
      return 'paid'
    case 'pending':
    case 'unpaid':
    case 'failed':
      return 'due'
    case 'partial':
      return 'partial'
    default:
      return 'muted'
  }
}

export const PAYMENT_TONE_CLASS: Record<PaymentTone, string> = {
  paid: 'bg-emerald-50 text-emerald-700 border-emerald-200',
  due: 'bg-red-50 text-red-700 border-red-200',
  partial: 'bg-amber-50 text-amber-700 border-amber-200',
  muted: 'bg-gray-50 text-gray-500 border-gray-200',
}
