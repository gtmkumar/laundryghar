/** Shared formatting helpers. */

/** ₹1,545 — integer rupees, Indian grouping, no decimals. */
export function rupees(amount: number): string {
  return `₹${Math.round(amount).toLocaleString('en-IN')}`;
}

/** "12 Jun 2026" */
export function formatDate(iso?: string): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleDateString('en-IN', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  } catch {
    return iso;
  }
}

/** "Fri 12:02" */
export function formatDateTime(iso?: string): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString('en-IN', {
      weekday: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

/** Greeting based on local hour. */
export function greeting(date = new Date()): string {
  const h = date.getHours();
  if (h < 12) return 'Good morning';
  if (h < 17) return 'Good afternoon';
  return 'Good evening';
}

/**
 * "2026-06-13" — LOCAL calendar date, NOT UTC.
 *
 * Never use `toISOString().slice(0, 10)` for date-only values: toISOString()
 * converts to UTC, so before 05:30 IST it returns *yesterday's* date.
 */
export function localDateIso(date = new Date()): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Mask a phone number for display: keeps the first 2 and last 4 digits,
 * masking the middle. "9812341234" → "+91 98 ●●●● 1234".
 * Falls back to the input when there are too few digits to mask meaningfully.
 */
export function maskPhone(raw?: string | null, countryPrefix = '+91'): string {
  const digits = (raw ?? '').replace(/\D/g, '');
  if (digits.length < 7) return raw ?? '';
  const lead = digits.slice(0, 2);
  const last4 = digits.slice(-4);
  const mask = '●'.repeat(digits.length - 6);
  return `${countryPrefix} ${lead} ${mask} ${last4}`;
}
