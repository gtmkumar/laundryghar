/**
 * Shared Zod schemas used across admin-web form drawers.
 *
 * These mirror the backend FluentValidation rules so that front-end errors
 * fire before the network round-trip. Any rule change here should be
 * kept in sync with the corresponding backend validator.
 *
 * Targeting Zod v4 — uses the unified `error` param instead of the v3
 * `required_error` / `invalid_type_error` split.
 */
import { z } from 'zod'

// ── Primitives ────────────────────────────────────────────────────────────────

/** Non-empty UUID string (as returned by react-select / <select> values). */
export const requiredId = z
  .string({ error: 'Required' })
  .min(1, 'Required')

/** Optional UUID string — empty string maps to undefined at submission time. */
export const optionalId = z.string().optional()

/** Required date string yyyy-MM-dd. */
export const requiredDate = z
  .string({ error: 'Required' })
  .min(1, 'Required')

/** Optional date string. */
export const optionalDate = z.string().optional()

/** Today as a yyyy-MM-dd string (local date), for comparing date inputs. */
function todayYmd(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * Optional date string yyyy-MM-dd that, when present, must be today or later.
 * Used for forward-looking dates like a rider's DL / insurance expiry — a
 * document that expired yesterday is a data-entry mistake, not a valid value.
 * Empty string = not provided (treated as optional).
 */
export const futureDate = z
  .string()
  .optional()
  .refine((v) => !v || v >= todayYmd(), { message: 'Date must be today or later' })

// ── Money ─────────────────────────────────────────────────────────────────────

/** Amount that must be strictly positive (> 0). */
export const positiveMoney = z
  .number({ error: 'Must be a number' })
  .gt(0, 'Must be greater than 0')

/** Amount that must be zero or positive (>= 0). */
export const nonNegativeMoney = z
  .number({ error: 'Must be a number' })
  .gte(0, 'Must be 0 or greater')

/** Non-negative integer capacity (e.g. daily pickups). */
export const nonNegativeInt = z
  .number({ error: 'Must be a number' })
  .int('Must be a whole number')
  .gte(0, 'Must be 0 or greater')

// ── Percentages ────────────────────────────────────────────────────────────────

/**
 * A percentage value clamped to 0..100 (royalty %, marketing %, rollout %, …).
 * Rejects out-of-range input instead of silently coercing it. Allows decimals.
 */
export const percentage = z
  .number({ error: 'Must be a number' })
  .gte(0, 'Must be 0 or greater')
  .lte(100, 'Must be 100 or less')

/**
 * A whole-number percentage clamped to 0..100 — for fields that only accept
 * integer percents (e.g. a staged rollout percentage).
 */
export const percentageInt = z
  .number({ error: 'Must be a number' })
  .int('Must be a whole number')
  .gte(0, 'Must be 0 or greater')
  .lte(100, 'Must be 100 or less')

// ── Web assets (CMS / banners) ──────────────────────────────────────────────────

/** Hex colour in #RRGGBB form. Optional; validates format only when non-empty. */
export const optionalHexColor = z
  .string()
  .optional()
  .refine((v) => !v || /^#([0-9a-fA-F]{6})$/.test(v), {
    message: 'Enter a 6-digit hex colour, e.g. #FFFFFF',
  })

/**
 * True for an absolute http(s) URL with a real host. Uses the URL parser (not a
 * loose regex) so junk like `http://=evil` or `https://` is rejected — a bare
 * `^https?:\/\/\S+$` would wave those through. Requires a dotted host or
 * `localhost` and no spaces.
 */
function isHttpUrl(v: string): boolean {
  try {
    const u = new URL(v)
    if (u.protocol !== 'http:' && u.protocol !== 'https:') return false
    if (/\s/.test(v)) return false
    const host = u.hostname
    return host === 'localhost' || /^[a-z0-9.-]+\.[a-z]{2,}$/i.test(host)
  } catch {
    return false
  }
}

/**
 * Imperative predicates for the same rules, for the handful of CMS forms that
 * validate inside an onSubmit handler rather than via a zod resolver. Keeps the
 * URL/hex rules in ONE place instead of divergent inline regexes per panel.
 */
export const isValidHttpUrl = (v: string): boolean => isHttpUrl(v)
export const isValidHexColor = (v: string): boolean => /^#([0-9a-fA-F]{6})$/.test(v)

/**
 * Parses `v` as a JSON object (non-array, non-null). Returns the parsed object,
 * or `null` when `v` is not a valid JSON object. Empty/whitespace returns an
 * empty object so callers can treat "blank" as "no variables".
 */
export function parseJsonObject(v: string): Record<string, unknown> | null {
  if (!v || !v.trim()) return {}
  try {
    const parsed: unknown = JSON.parse(v)
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

/** Absolute http(s) URL. Optional; validates format only when non-empty. */
export const optionalUrl = z
  .string()
  .optional()
  .refine((v) => !v || isHttpUrl(v), {
    message: 'Enter a valid http(s) URL',
  })

/** Required absolute http(s) URL (e.g. a banner's mandatory image URL). */
export const requiredUrl = z
  .string({ error: 'Required' })
  .min(1, 'Required')
  .refine((v) => isHttpUrl(v), { message: 'Enter a valid http(s) URL' })

/**
 * A JSON-object string: must parse to a plain (non-array, non-null) object.
 * For jsonb columns that reject bare strings/arrays (e.g. notification template
 * `variables`). Empty string = not provided (treated as optional). Mirrors the
 * house convention used by the catalog/subscription localized-name helpers.
 */
export const optionalJsonObject = z
  .string()
  .optional()
  .refine(
    (v) => {
      if (!v || !v.trim()) return true
      try {
        const parsed: unknown = JSON.parse(v)
        return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      } catch {
        return false
      }
    },
    { message: 'Enter a valid JSON object, e.g. {"key":"value"}' },
  )

// ── Contact / identity ────────────────────────────────────────────────────────

/**
 * Email — optional at the field level.
 * When present it must look like a valid address.
 */
export const optionalEmail = z
  .string()
  .optional()
  .refine(
    (v) => !v || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v),
    { message: 'Enter a valid email address' },
  )

/**
 * Required email.
 */
export const requiredEmail = z
  .string({ error: 'Email is required' })
  .min(1, 'Email is required')
  .email('Enter a valid email address')

/**
 * Phone — E.164 friendly.
 * Accepts "+91 XXXXX XXXXX" (spaces stripped) or "+91XXXXXXXXXX".
 * Also accepts plain 10-digit Indian mobile numbers.
 * Empty string = not provided (treated as optional).
 */
export const optionalPhone = z
  .string()
  .optional()
  .refine(
    (v) => {
      if (!v) return true
      const stripped = v.replace(/\s+/g, '')
      return /^\+?[1-9]\d{7,14}$/.test(stripped)
    },
    { message: 'Enter a valid phone number (e.g. +91 98765 43210)' },
  )

/** PAN format: AAAAA0000A (5 uppercase letters, 4 digits, 1 uppercase letter). */
export const optionalPan = z
  .string()
  .optional()
  .refine(
    (v) => !v || /^[A-Z]{5}[0-9]{4}[A-Z]$/.test(v),
    { message: 'PAN must be in the format AAAAA0000A' },
  )

/** IFSC format: 4 uppercase letters + 0 + 6 alphanumeric chars. */
export const optionalIfsc = z
  .string()
  .optional()
  .refine(
    (v) => !v || /^[A-Z]{4}0[A-Z0-9]{6}$/.test(v),
    { message: 'IFSC must be in the format ABCD0123456' },
  )

/** UPI handle: anything@anything. */
export const optionalUpi = z
  .string()
  .optional()
  .refine(
    (v) => !v || /^[a-zA-Z0-9.\-_]{2,256}@[a-zA-Z]{2,64}$/.test(v),
    { message: 'Enter a valid UPI handle (e.g. name@upi)' },
  )

/** 6-digit Indian pincode. */
export const optionalPincode = z
  .string()
  .optional()
  .refine(
    (v) => !v || /^\d{6}$/.test(v),
    { message: 'Pincode must be exactly 6 digits' },
  )
