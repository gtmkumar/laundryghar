/**
 * Value-slab (declared-value) helpers — GitHub #22.
 *
 * Some catalog items are priced by a customer-declared garment value rather than a
 * fixed price-list rate (`pricingMode === 'value_slab'`). The listed base price is a
 * placeholder; the real price is resolved server-side from the declared value against
 * the brand's value slabs at order time.
 *
 * The booking flow collects the declared value up-front, but the server is the
 * authority and can still reject a create with an HTTP 422 structured error:
 *   - `declared_value_required` — a value_slab item was sent without a positive value.
 *       fields: { itemId, itemName }
 *   - `no_value_slab_match` — no slab covers the declared value.
 *       fields: { itemId, declaredValue }
 * Both share the same envelope as `min_order_value_not_met` (each field is a
 * single-element string array under `message.errorMessage`, with `message.responseMessage`
 * carrying the human-readable text). {@link parseValueSlabError} maps them to a typed
 * shape so the pay flow can name the offending item and re-prompt for its value.
 */
import { ApiError } from '@/api/client';

export type ValueSlabErrorCode = 'declared_value_required' | 'no_value_slab_match';

export interface ValueSlabViolation {
  code: ValueSlabErrorCode;
  /** Catalog item id the error refers to — matches CartLine.itemId (not the local cart key). */
  itemId: string | null;
  /** Item name, present on declared_value_required. */
  itemName?: string;
  /** The rejected declared value, present on no_value_slab_match. */
  declaredValue?: number;
  /** Human-readable server message, when present. */
  message?: string;
}

/** Read the first element of a `string[]` machine field as a string. */
function firstString(field: unknown): string | null {
  const raw = Array.isArray(field) ? field[0] : field;
  return raw == null ? null : String(raw);
}

/** Read the first element of a `string[]` machine field and parse it as a decimal. */
function firstNumber(field: unknown): number | null {
  const raw = firstString(field);
  if (raw == null) return null;
  const n = Number.parseFloat(raw);
  return Number.isFinite(n) ? n : null;
}

/**
 * Detect a value-slab rejection (`declared_value_required` / `no_value_slab_match`) on a
 * failed order/schedule call and extract its fields. Handles both the raw Axios 422
 * envelope ({ message: { errorMessage: {...} } }) and an unwrapped {@link ApiError}.
 * Returns null for any other error.
 */
export function parseValueSlabError(err: unknown): ValueSlabViolation | null {
  const fromApiError = err instanceof ApiError ? err.fieldErrors : undefined;
  const axiosData = (
    err as { response?: { data?: { message?: { errorMessage?: Record<string, unknown> } } } }
  )?.response?.data?.message?.errorMessage;

  const fields = fromApiError ?? axiosData;
  if (!fields) return null;

  const codeStr = firstString(fields['code']);
  if (codeStr !== 'declared_value_required' && codeStr !== 'no_value_slab_match') {
    return null;
  }

  const message = (
    err as { response?: { data?: { message?: { responseMessage?: string } } } }
  )?.response?.data?.message?.responseMessage;

  const declaredValue = firstNumber(fields['declaredValue']);

  return {
    code: codeStr,
    itemId: firstString(fields['itemId']),
    itemName: firstString(fields['itemName']) ?? undefined,
    declaredValue: declaredValue ?? undefined,
    message: typeof message === 'string' ? message : undefined,
  };
}
