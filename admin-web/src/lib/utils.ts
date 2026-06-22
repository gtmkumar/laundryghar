import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/**
 * Derive a stable uppercase code/SKU from a human name.
 *   "Dry Clean"   → "DRY-CLEAN"
 *   "Men's wear"  → "MENS-WEAR"
 *   "Wash & Fold" → "WASH-FOLD"
 * Used to auto-suggest the Code/SKU field from the Name as the user types.
 */
export function slugifyCode(name: string): string {
  return name
    .trim()
    .toUpperCase()
    .replace(/['’`]/g, '')        // drop apostrophes so "Men's" → "MENS"
    .replace(/[^A-Z0-9]+/g, '-')  // any run of non-alphanumerics → single hyphen
    .replace(/^-+|-+$/g, '')      // trim leading/trailing hyphens
    .slice(0, 50)
}

export function formatCurrency(amount: number, currency = 'INR'): string {
  // Intl throws RangeError on malformed codes (e.g. bad data in a single row),
  // which would crash the whole page render — fall back to INR instead.
  try {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
    }).format(amount)
  } catch {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      minimumFractionDigits: 2,
    }).format(amount)
  }
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
