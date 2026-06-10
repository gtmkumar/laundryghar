/**
 * Tiny inline validation-error paragraph used beneath form fields.
 * Works with react-hook-form's `fieldState.error.message`.
 *
 * Pass an `id` and reference it from the input's `aria-describedby` (together
 * with `aria-invalid`) so screen readers announce the error and associate it
 * with the field. `role="alert"` makes the message announce as it appears.
 *
 * Usage:
 *   <input
 *     aria-invalid={!!errors.email}
 *     aria-describedby={errors.email ? 'email-error' : undefined}
 *   />
 *   <FieldError id="email-error" message={errors.email?.message} />
 */
export function FieldError({ id, message }: { id?: string; message?: string }) {
  if (!message) return null
  return (
    <p id={id} role="alert" className="mt-1 text-xs text-red-600">
      {message}
    </p>
  )
}
