/**
 * Minimum-order helpers (GitHub #23).
 *
 * The catalog config exposes `minOrderValue` (nullable ⇒ no restriction). When
 * the cart's ITEM subtotal is below it, the booking flow blocks the place-order
 * action and shows a bottom sheet. As a defensive backstop, the server can also
 * reject scheduling with HTTP 422 `min_order_value_not_met` (e.g. stale config);
 * `parseMinOrderError` maps that envelope to the same sheet instead of a toast.
 */
import { ApiError } from '@/api/client';
import type { CatalogConfigDto } from '@/types/api';

/** Currency symbols for the codes we ship with; falls back to the raw code. */
const CURRENCY_SYMBOLS: Record<string, string> = {
  INR: '₹',
  USD: '$',
  EUR: '€',
  GBP: '£',
  AED: 'AED ',
};

/** Format an integer amount for the given ISO currency code — no hardcoded symbol. */
export function formatCurrency(amount: number, currencyCode: string): string {
  const rounded = Math.round(amount).toLocaleString('en-IN');
  const symbol = CURRENCY_SYMBOLS[(currencyCode ?? '').toUpperCase()];
  return symbol ? `${symbol}${rounded}` : `${currencyCode} ${rounded}`.trim();
}

export interface MinOrderGate {
  /** True when a minimum is configured AND the subtotal is below it. */
  blocked: boolean;
  minOrderValue: number;
  currencyCode: string;
  /** How much more (in currency units) the customer must add. */
  shortfall: number;
}

/**
 * Evaluate the item subtotal against the catalog config. Returns null when there
 * is no restriction (config missing/loading, or `minOrderValue` null/≤0), so the
 * caller shows nothing.
 */
export function evaluateMinOrder(
  subtotal: number,
  config: CatalogConfigDto | undefined,
): MinOrderGate | null {
  const min = config?.minOrderValue;
  if (config == null || min == null || min <= 0) return null;
  const shortfall = Math.max(0, min - subtotal);
  return {
    blocked: subtotal < min,
    minOrderValue: min,
    currencyCode: config.currencyCode,
    shortfall,
  };
}

/** Read the first element of a `string[]` machine field and parse it as a decimal. */
function firstNumber(field: unknown): number | null {
  const raw = Array.isArray(field) ? field[0] : field;
  if (raw == null) return null;
  const n = Number.parseFloat(String(raw));
  return Number.isFinite(n) ? n : null;
}

export interface MinOrderViolation {
  minimum: number;
  subtotal: number;
  shortfall: number;
  /** Human-readable server message, when present. */
  message?: string;
}

/**
 * Detect a `min_order_value_not_met` rejection on a failed schedule/order call
 * and extract its numeric fields. Handles both the raw Axios 422 envelope
 * ({ message: { errorMessage: { code, minimum, subtotal, shortfall } } }) and an
 * unwrapped {@link ApiError}. Returns null for any other error.
 */
export function parseMinOrderError(err: unknown): MinOrderViolation | null {
  // ApiError carries the envelope's errorMessage map as fieldErrors.
  const fromApiError =
    err instanceof ApiError ? err.fieldErrors : undefined;

  // Raw Axios error: envelope lives on response.data.message.errorMessage.
  const axiosData = (
    err as { response?: { data?: { message?: { errorMessage?: Record<string, unknown> } } } }
  )?.response?.data?.message?.errorMessage;

  const fields = fromApiError ?? axiosData;
  if (!fields) return null;

  const code = fields['code'];
  const codeStr = Array.isArray(code) ? code[0] : code;
  if (codeStr !== 'min_order_value_not_met') return null;

  const minimum = firstNumber(fields['minimum']);
  const subtotal = firstNumber(fields['subtotal']);
  let shortfall = firstNumber(fields['shortfall']);
  if (shortfall == null && minimum != null && subtotal != null) {
    shortfall = Math.max(0, minimum - subtotal);
  }

  const message = (
    err as { response?: { data?: { message?: { responseMessage?: string } } } }
  )?.response?.data?.message?.responseMessage;

  return {
    minimum: minimum ?? 0,
    subtotal: subtotal ?? 0,
    shortfall: shortfall ?? 0,
    message: typeof message === 'string' ? message : undefined,
  };
}
