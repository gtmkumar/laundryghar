/**
 * Helpers for the backend's error envelope on a rejected request:
 *
 *   {
 *     status: false,
 *     message: {
 *       errorTypeCode: 409 | 422 | 404 | …,
 *       errorMessage: { Field: [msg, …] } | { error: [msg] },
 *       responseMessage: "One or more validation errors occurred"
 *     }
 *   }
 *
 * Axios rejects with the raw error, so `error.message` is the generic
 * "Request failed with status code 4xx". These helpers walk the response body
 * to surface the validator text and field-level details, falling back
 * gracefully when the body has another shape (network error, proxy HTML, …).
 */

interface ErrorEnvelope {
  errorTypeCode?: number
  errorMessage?: Record<string, string[] | string>
  responseMessage?: string
}

function envelopeOf(error: unknown): ErrorEnvelope | undefined {
  const body = (error as { response?: { data?: unknown } })?.response?.data as
    | { message?: ErrorEnvelope }
    | undefined
  const env = body?.message
  return env && typeof env === 'object' ? env : undefined
}

/** HTTP status of a rejected axios call, or undefined (network error etc.). */
export function apiErrorStatus(error: unknown): number | undefined {
  return (error as { response?: { status?: number } })?.response?.status
}

/**
 * Detects the backend's §8 "step-up required" 403. High/critical actions reject
 * with `{ responseMessage: "step_up_required", errorMessage: { step_up_required:
 * ["<permission.code>", …] } }`. Returns the permission codes that need a fresh
 * OTP re-verification (empty array if the list is absent), or `null` when this is
 * an ordinary permission denial. The permission codes are informational — the
 * fix is the same regardless of which one tripped: re-verify and retry.
 */
export function stepUpRequiredPermissions(error: unknown): string[] | null {
  if (apiErrorStatus(error) !== 403) return null
  const envelope = envelopeOf(error)
  if (envelope?.responseMessage !== 'step_up_required') return null
  const raw = envelope.errorMessage?.step_up_required
  if (Array.isArray(raw)) return raw.filter((c): c is string => typeof c === 'string')
  return typeof raw === 'string' ? [raw] : []
}

/**
 * Field-level validation errors keyed by the backend's field name (PascalCase
 * or camelCase, e.g. `Price` / `nameLocalized`). The generic `error` key the
 * backend uses for non-field failures is excluded — that belongs in the
 * banner/toast, not next to an input. Returns {} when there are none.
 */
export function apiFieldErrors(error: unknown): Record<string, string[]> {
  const fieldErrors = envelopeOf(error)?.errorMessage
  if (!fieldErrors || typeof fieldErrors !== 'object') return {}
  const out: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(fieldErrors)) {
    if (key.toLowerCase() === 'error') continue // general message, not a field
    const msgs = (Array.isArray(value) ? value : [value]).filter(
      (m): m is string => typeof m === 'string' && m.length > 0,
    )
    if (msgs.length) out[key] = msgs
  }
  return out
}

/**
 * True when a rejected request carries a specific backend error code (e.g.
 * `value_slab_overlap`, `item_code_taken`). The backend surfaces these either as
 * the envelope's `responseMessage`, as an `errorMessage` key, or embedded in an
 * `errorMessage` value — we check all three so a caller can branch on the code
 * regardless of exactly where the backend put it. Case-insensitive.
 */
export function apiErrorHasCode(error: unknown, code: string): boolean {
  const envelope = envelopeOf(error)
  if (!envelope) return false
  const needle = code.toLowerCase()
  if (envelope.responseMessage?.toLowerCase() === needle) return true
  const fieldErrors = envelope.errorMessage
  if (fieldErrors && typeof fieldErrors === 'object') {
    for (const key of Object.keys(fieldErrors)) {
      if (key.toLowerCase() === needle) return true
    }
    for (const value of Object.values(fieldErrors)) {
      const msgs = Array.isArray(value) ? value : [value]
      if (msgs.some((m) => typeof m === 'string' && m.toLowerCase().includes(needle))) return true
    }
  }
  return false
}

/** Pulls a human-readable message out of a rejected request. */
export function apiErrorMessage(error: unknown, fallback = 'Something went wrong.'): string {
  const envelope = envelopeOf(error)
  if (envelope) {
    const fieldErrors = envelope.errorMessage
    if (fieldErrors && typeof fieldErrors === 'object') {
      const flat = Object.values(fieldErrors)
        .flat()
        .filter((m): m is string => typeof m === 'string' && m.length > 0)
      if (flat.length) return flat.join(' ')
    }
    if (envelope.responseMessage) return envelope.responseMessage
  }

  if (error instanceof Error && error.message) return error.message
  return fallback
}
