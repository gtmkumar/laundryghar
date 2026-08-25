/**
 * Per-vertical vocabulary from the server (PLATFORM_STRATEGY.md §3 "terminology is config, not code").
 *
 * §12 names terminology leakage as a top product risk — "laundry words in a courier UI kills
 * credibility" — and it fails silently: nothing errors, the app just reads wrong. Words therefore
 * come from `GET /api/v1/terminology` rather than being hardcoded per app.
 *
 * Every read falls back to a neutral default, because terminology is fetched asynchronously and a
 * screen must never blank waiting for a noun.
 */
export interface Term {
  singular: string;
  plural: string;
}

export interface TerminologyPack {
  verticalKey: string;
  terms: Record<string, Term>;
}

/** A word from the pack, or `fallback` if the pack is absent or lacks the key. */
export function term(
  pack: TerminologyPack | undefined,
  key: string,
  fallback: string,
  form: 'singular' | 'plural' = 'singular',
): string {
  const entry = pack?.terms?.[key];
  if (!entry) return fallback;
  return (form === 'plural' ? entry.plural : entry.singular) || fallback;
}

/** "garment" / "service" / "parcel" / "meal" — what the customer hands over or receives. */
export function itemNoun(
  pack: TerminologyPack | undefined,
  form: 'singular' | 'plural' = 'singular',
): string {
  return term(pack, 'item', form === 'plural' ? 'items' : 'item', form);
}

/** "order" / "appointment" / "shipment" / "delivery" — the thing a customer creates. */
export function bookingNoun(
  pack: TerminologyPack | undefined,
  form: 'singular' | 'plural' = 'singular',
): string {
  return term(pack, 'booking', form === 'plural' ? 'bookings' : 'booking', form);
}

/** "Warehouse" / "Studio" / "Hub" / "Kitchen" — the on-site processing or service location. */
export function onsiteLocationNoun(pack: TerminologyPack | undefined): string {
  return term(pack, 'onsite_location', 'Location');
}

/** `3` + itemNoun -> "3 garments". Pluralises on the count, which is where a missing plural shows. */
export function countOfItems(pack: TerminologyPack | undefined, count: number): string {
  return `${count} ${itemNoun(pack, count === 1 ? 'singular' : 'plural')}`;
}
