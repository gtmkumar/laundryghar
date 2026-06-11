import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function formatCurrency(amount: number, currency = 'INR'): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount)
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  })
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

/** Returns "YYYY-MM-DD" for today in Asia/Kolkata timezone */
export function todayLocalDate(): string {
  return new Date()
    .toLocaleDateString('en-CA', { timeZone: 'Asia/Kolkata' })
}

/**
 * Best human label for a customer: displayName → first+last → phone → code.
 * Mirrors the precedence admin-web uses for its customer name map.
 */
export function customerLabel(c: {
  displayName?: string | null
  firstName?: string | null
  lastName?: string | null
  phoneE164?: string | null
  customerCode?: string | null
}): string {
  if (c.displayName?.trim()) return c.displayName.trim()
  const full = [c.firstName, c.lastName].filter(Boolean).join(' ').trim()
  if (full) return full
  if (c.phoneE164?.trim()) return c.phoneE164.trim()
  return c.customerCode?.trim() || 'Customer'
}

/**
 * Generates an RFC-4122 v4 UUID for client-side idempotency keys.
 * POS-2: order-create and payment-record each carry a fresh key so a double-tap
 * or an axios retry on flaky store wifi can't create a duplicate order/charge —
 * the backend dedupes on the key. Uses the platform crypto.randomUUID when
 * available (all evergreen browsers), with a Math.random fallback for safety.
 */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

/**
 * Normalizes a counter-typed phone to E.164.
 * POS-7: staff routinely type a bare 10-digit Indian mobile; reject only when it
 * can't be made into a valid number. Accepts: already-E.164 (+9198…), a bare
 * 10-digit number (auto-prefixed +91), or 91-prefixed 12-digit. Returns null
 * when the input can't be normalized so the caller can show inline validation.
 */
export function normalizePhoneE164(raw: string): string | null {
  const trimmed = raw.trim()
  if (!trimmed) return null
  // Already E.164.
  if (/^\+[1-9]\d{6,14}$/.test(trimmed)) return trimmed
  // Strip spaces, dashes, parens for digit analysis.
  const digits = trimmed.replace(/[\s\-()]/g, '')
  // Bare 10-digit Indian mobile (starts 6-9).
  if (/^[6-9]\d{9}$/.test(digits)) return `+91${digits}`
  // 91-prefixed 12-digit without the plus.
  if (/^91[6-9]\d{9}$/.test(digits)) return `+${digits}`
  return null
}

export function orderStatusColor(status: string): string {
  const map: Record<string, string> = {
    placed: 'bg-blue-100 text-blue-800',
    picked_up: 'bg-indigo-100 text-indigo-800',
    received: 'bg-purple-100 text-purple-800',
    sorting: 'bg-yellow-100 text-yellow-800',
    in_process: 'bg-orange-100 text-orange-800',
    qc: 'bg-cyan-100 text-cyan-800',
    ready: 'bg-green-100 text-green-800',
    out_for_delivery: 'bg-teal-100 text-teal-800',
    delivered: 'bg-green-200 text-green-900',
    cancelled: 'bg-red-100 text-red-800',
    returned: 'bg-gray-100 text-gray-800',
    rewash: 'bg-pink-100 text-pink-800',
  }
  return map[status] ?? 'bg-gray-100 text-gray-800'
}

/** Returns allowed next statuses for a given current order status (POS-relevant transitions). */
export function nextStatuses(current: string): string[] {
  const map: Record<string, string[]> = {
    placed: ['received', 'cancelled'],
    picked_up: ['received', 'cancelled'],
    received: ['sorting'],
    sorting: ['in_process'],
    in_process: ['qc'],
    qc: ['ready', 'rewash'],
    ready: ['out_for_delivery', 'delivered'],
    out_for_delivery: ['delivered'],
    rewash: ['in_process'],
  }
  return map[current] ?? []
}
