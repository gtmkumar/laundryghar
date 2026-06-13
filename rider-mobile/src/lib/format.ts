/** Shared display-formatting helpers (pure — unit tested). */

/** "s" when count !== 1 — for simple English pluralisation via i18n {{plural}}. */
export function pluralSuffix(count: number): string {
  return count === 1 ? '' : 's';
}

/** Known vehicle enum values → human labels. */
const VEHICLE_LABELS: Record<string, string> = {
  two_wheeler: 'Two-wheeler',
  three_wheeler: 'Three-wheeler',
  four_wheeler: 'Four-wheeler',
  bicycle: 'Bicycle',
  van: 'Van',
};

/**
 * Humanise a vehicle-type enum: "two_wheeler" → "Two-wheeler".
 * Unknown values degrade to snake_case → "Sentence case" ("e_rickshaw" → "E rickshaw").
 */
export function humanizeVehicleType(value?: string | null): string {
  if (!value) return '';
  const known = VEHICLE_LABELS[value.toLowerCase()];
  if (known) return known;
  const words = value.replace(/_/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * Format an Indian phone number for display:
 *   "+919876543210" / "9876543210" → "+91 98765 43210".
 * Anything that is not a 10-digit (optionally +91-prefixed) number is returned as-is.
 */
export function formatPhone(value?: string | null): string {
  if (!value) return '';
  const digits = value.replace(/\D/g, '');

  let local: string | null = null;
  if (digits.length === 12 && digits.startsWith('91')) {
    local = digits.slice(2); // +91XXXXXXXXXX / 91XXXXXXXXXX
  } else if (digits.length === 10 && !value.trim().startsWith('+')) {
    local = digits; // bare local number
  }

  // Indian mobile numbers start 6-9 — anything else passes through untouched.
  if (!local || !/^[6-9]/.test(local)) return value;
  return `+91 ${local.slice(0, 5)} ${local.slice(5)}`;
}
