/**
 * Vertical-aware terminology for user-management (multi-vertical Phase 3+).
 *
 * The Access-Control console was written for laundry and hardcodes laundry words — chiefly
 * "Warehouse" (the on-site processing location) and the `warehouse_staff` user_type. On a salon or
 * logistics brand those read wrong. This module is the single source of truth that maps a brand's
 * `verticalKey` → the right nouns/labels and the right neutral user_type, so every component can stay
 * vertical-correct. Pure data (no React) so it's trivially testable and reusable.
 *
 * Scope: only the genuinely vertical-specific vocabulary varies here (the on-site location + the
 * on-site staff user_type + an example designation). "Franchise", "Store", "Brand" and "Platform" are
 * platform-wide tenancy concepts shared across verticals and are intentionally left stable.
 */
import { UserType } from '@/types/userType'

export const VERTICAL = {
  laundry: 'laundry',
  salon: 'salon',
  logistics: 'logistics',
  tiffin: 'tiffin',
} as const

export type VerticalKey = (typeof VERTICAL)[keyof typeof VERTICAL]

/**
 * FALLBACK vocabulary only.
 *
 * The authoritative source is the server's terminology pack (`GET /api/v1/terminology`, backed by
 * identity_access.vertical_terms) — PLATFORM_STRATEGY.md §3 makes terminology config, not code, so a
 * provider who calls them "pieces" rather than "garments" is a row, not a deploy. These constants
 * remain as the offline default so a screen renders a sensible word while the pack is loading, or if
 * the request fails. Prefer `termFrom(pack, ...)` wherever a pack is available.
 */
const ONSITE_NOUN: Record<string, string> = {
  laundry: 'Warehouse',
  salon: 'Studio',
  logistics: 'Hub',
  // Recurring-schedule vertical (Mode 3): the on-site location is where meals are prepared.
  tiffin: 'Kitchen',
}

/** Example designation/job-title placeholder, per vertical. */
const DESIGNATION_EG: Record<string, string> = {
  laundry: 'e.g. Store Supervisor',
  salon: 'e.g. Senior Stylist',
  logistics: 'e.g. Hub Operations Lead',
  tiffin: 'e.g. Kitchen Supervisor',
}

function normalize(verticalKey: string | null | undefined): string {
  return verticalKey && verticalKey in ONSITE_NOUN ? verticalKey : VERTICAL.laundry
}

/** The on-site location noun for a vertical — "Warehouse" / "Studio" / "Hub". */
export function onsiteNoun(verticalKey: string | null | undefined): string {
  return ONSITE_NOUN[normalize(verticalKey)]
}

/**
 * The `user_type` to assign to on-site processing/service staff for a vertical. Laundry keeps the
 * legacy `warehouse_staff` (seeded roles + DB CHECKs reference it); every other vertical uses the
 * neutral successor `ops_staff`. Mirrors the backend guidance on SharedDataModel/Enums/UserType.
 */
export function onsiteUserType(verticalKey: string | null | undefined): string {
  return normalize(verticalKey) === VERTICAL.laundry ? UserType.warehouseStaff : UserType.opsStaff
}

/** A job-title placeholder appropriate to the vertical. */
export function designationPlaceholder(verticalKey: string | null | undefined): string {
  return DESIGNATION_EG[normalize(verticalKey)]
}

/** Full per-scope display label (e.g. a role's scope chip), vertical-aware for the on-site scope. */
export function scopeLabel(scopeType: string | null | undefined, verticalKey?: string | null): string {
  switch (scopeType) {
    case 'platform': return 'Platform-wide'
    case 'brand': return 'Enterprise-wide'
    case 'franchise': return 'Franchise'
    case 'store': return 'Store'
    case 'warehouse': return onsiteNoun(verticalKey)
    default: return scopeType ?? '—'
  }
}

/** "…-scoped" variant used in the Roles tab header. */
export function scopeScopedLabel(scopeType: string | null | undefined, verticalKey?: string | null): string {
  switch (scopeType) {
    case 'platform': return 'Platform-wide'
    case 'brand': return 'Enterprise-wide'
    case 'franchise': return 'Franchise-scoped'
    case 'store': return 'Store-scoped'
    case 'warehouse': return `${onsiteNoun(verticalKey)}-scoped`
    default: return scopeType ?? '—'
  }
}

/** Options for the role-form scope <select>, with a vertical-aware on-site label. */
export function roleScopeOptions(verticalKey?: string | null): { value: string; label: string }[] {
  return [
    { value: 'brand', label: 'Brand (enterprise-wide)' },
    { value: 'franchise', label: 'Franchise' },
    { value: 'store', label: 'Store' },
    { value: 'warehouse', label: onsiteNoun(verticalKey) },
  ]
}


// ─── Server-driven terminology (the authoritative path) ─────────────────────

/** The shape returned by `GET /api/v1/terminology`. */
export interface TermPack {
  verticalKey: string
  terms: Record<string, { singular: string; plural: string }>
}

/**
 * A word from the server pack, falling back to `fallback` when the pack has not loaded or does not
 * define the key.
 *
 * The fallback is not defensive clutter: terminology is fetched asynchronously and a screen must
 * never blank or flash empty waiting for a noun. A slightly generic word for one render is a much
 * smaller failure than a blank one.
 */
export function termFrom(
  pack: TermPack | undefined,
  key: string,
  fallback: string,
  form: 'singular' | 'plural' = 'singular',
): string {
  const term = pack?.terms?.[key]
  if (!term) return fallback
  return (form === 'plural' ? term.plural : term.singular) || fallback
}

/** The item noun — "garment" / "service" / "parcel" / "meal". */
export function itemNoun(pack: TermPack | undefined, form: 'singular' | 'plural' = 'singular'): string {
  return termFrom(pack, 'item', form === 'plural' ? 'items' : 'item', form)
}

/** The booking noun — "order" / "appointment" / "shipment" / "delivery". */
export function bookingNoun(pack: TermPack | undefined, form: 'singular' | 'plural' = 'singular'): string {
  return termFrom(pack, 'booking', form === 'plural' ? 'bookings' : 'booking', form)
}

/**
 * The on-site location noun, preferring the server pack and falling back to the local map.
 * Supersedes {@link onsiteNoun} at call sites that can reach a pack.
 */
export function onsiteNounFrom(pack: TermPack | undefined, verticalKey?: string | null): string {
  return termFrom(pack, 'onsite_location', onsiteNoun(verticalKey))
}
