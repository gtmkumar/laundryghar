/**
 * Pulls a human-readable message out of a rejected request.
 *
 * The backend envelope on a failed call is
 *   { status: false, message: { errorMessage: { Field: [msg, …] }, responseMessage } }
 * Axios rejects with the raw error, so `error.message` is the generic
 * "Request failed with status code 4xx". This walks the response body to surface
 * the validator message (e.g. the name_localized 422) and falls back gracefully.
 */
export function apiErrorMessage(error: unknown, fallback = 'Something went wrong.'): string {
  const body = (error as { response?: { data?: unknown } })?.response?.data as
    | { message?: { errorMessage?: Record<string, string[]>; responseMessage?: string } }
    | undefined

  const envelope = body?.message
  if (envelope) {
    const fieldErrors = envelope.errorMessage
    if (fieldErrors && typeof fieldErrors === 'object') {
      const flat = Object.values(fieldErrors).flat().filter(Boolean)
      if (flat.length) return flat.join(' ')
    }
    if (envelope.responseMessage) return envelope.responseMessage
  }

  if (error instanceof Error && error.message) return error.message
  return fallback
}
