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
