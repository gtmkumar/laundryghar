/**
 * The API envelope carries two competing error strings, and picking the wrong one is a
 * silent UX regression: a 422 puts the actionable text in `errorMessage` (per field) while
 * `responseMessage` is only the generic "One or more validation errors occurred" wrapper.
 *
 * Caught live: setting the PIN to "1234" showed the wrapper instead of the server's
 * "That PIN is too easy to guess." — these tests pin the precedence.
 */
import { envelopeMessage } from '@/api/auth';
import type { SingleResponse } from '@/types/api';

const envelope = (message: SingleResponse<unknown>['message']): SingleResponse<unknown> => ({
  status: false,
  message,
});

describe('envelopeMessage', () => {
  it('prefers the field-level validation text over the generic wrapper', () => {
    // Verbatim shape returned by POST /customer/auth/pin for a forbidden PIN.
    expect(
      envelopeMessage(
        envelope({
          errorMessage: { Pin: ['That PIN is too easy to guess. Please choose another.'] },
          responseMessage: 'One or more validation errors occurred',
        }),
      ),
    ).toBe('That PIN is too easy to guess. Please choose another.');
  });

  it('joins multiple field errors so none are silently dropped', () => {
    expect(
      envelopeMessage(
        envelope({
          errorMessage: { Phone: ['Phone must be in E.164 format.'], Code: ['OTP must be exactly 4 digits.'] },
          responseMessage: 'One or more validation errors occurred',
        }),
      ),
    ).toBe('Phone must be in E.164 format. OTP must be exactly 4 digits.');
  });

  it('falls back to responseMessage when there are no field errors', () => {
    // e.g. a BusinessRuleException, which has no per-field breakdown.
    expect(
      envelopeMessage(
        envelope({ responseMessage: 'That mobile number is already registered to another account.' }),
      ),
    ).toBe('That mobile number is already registered to another account.');
  });

  it('ignores an empty errorMessage map rather than returning a blank string', () => {
    expect(envelopeMessage(envelope({ errorMessage: {}, responseMessage: 'Unauthorized.' })))
      .toBe('Unauthorized.');
  });

  it('skips blank field strings', () => {
    expect(envelopeMessage(envelope({ errorMessage: { Pin: [''] }, responseMessage: 'Fallback.' })))
      .toBe('Fallback.');
  });

  it('returns null when the envelope carries nothing usable, so callers apply their own copy', () => {
    expect(envelopeMessage(undefined)).toBeNull();
    expect(envelopeMessage(envelope(undefined))).toBeNull();
    expect(envelopeMessage(envelope({}))).toBeNull();
  });
});
