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
