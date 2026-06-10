/**
 * `name_localized` is a Postgres **jsonb** column on every catalog entity
 * (service_categories, services, items, fabric_types, item_groups, add_ons).
 * Postgres rejects a bare string with `400 / 22P02`, so the value must be a
 * JSON-object string. The house shape is `{"en":"…","hi":"…"}` (the seeder and
 * sibling drawers — packages, subscriptions — all use it). `hi` is omitted when
 * empty so we never persist a blank Hindi key.
 */
export function buildNameLocalized(en: string, hi: string): string {
  const obj: Record<string, string> = {}
  if (en.trim()) obj.en = en.trim()
  if (hi.trim()) obj.hi = hi.trim()
  return JSON.stringify(obj)
}

/** Read `{ en, hi }` back out of the stored jsonb string for edit prefills. */
export function parseNameLocalized(raw: string | null | undefined): { en: string; hi: string } {
  if (!raw) return { en: '', hi: '' }
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>
    return {
      en: typeof obj.en === 'string' ? obj.en : '',
      hi: typeof obj.hi === 'string' ? obj.hi : '',
    }
  } catch {
    // Legacy rows may hold a plain string — treat it as the English name.
    return { en: raw, hi: '' }
  }
}

/** A friendly display string for a localized jsonb value (English, then raw). */
export function displayLocalized(raw: string | null | undefined): string {
  return parseNameLocalized(raw).en
}

/** date-only (yyyy-mm-dd) slice of an ISO instant, for <input type="date">. */
export function dateOnly(iso: string | null | undefined): string {
  return iso ? iso.slice(0, 10) : ''
}

/** A yyyy-mm-dd date back to a UTC midnight ISO instant for the API. */
export function toInstant(date: string): string {
  return new Date(`${date}T00:00:00Z`).toISOString()
}
