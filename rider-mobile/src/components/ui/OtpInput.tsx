/**
 * OtpInput — segmented code entry.
 *
 * A single hidden TextInput captures keystrokes; the visible boxes are a
 * presentation layer. Used by the login OTP screen and the delivery-OTP
 * confirmation card. Digits only.
 */
import React, { useRef, useState } from 'react';
import { Pressable, Text, TextInput, View } from 'react-native';

interface OtpInputProps {
  value:        string;
  onChangeText: (next: string) => void;
  length?:      number;
  autoFocus?:   boolean;
  /** Fired when the value reaches `length` digits. */
  onComplete?:  (code: string) => void;
  /** Visual error state (red borders). */
  hasError?:    boolean;
  /** "olive" (auth) | "gold" (delivery) accent for the focused cell. */
  accent?:      'olive' | 'gold';
  /**
   * When false, the segmented boxes are display-only (no system keyboard) —
   * the parent drives `value` via a custom keypad. The cell at the current
   * cursor still shows an active highlight.
   */
  editable?:    boolean;
}

export function OtpInput({
  value,
  onChangeText,
  length = 4,
  autoFocus = false,
  onComplete,
  hasError = false,
  accent = 'olive',
  editable = true,
}: OtpInputProps) {
  const inputRef = useRef<TextInput>(null);
  const [focused, setFocused] = useState(autoFocus);

  const focus = () => editable && inputRef.current?.focus();

  const handleChange = (raw: string) => {
    const digits = raw.replace(/[^0-9]/g, '').slice(0, length);
    onChangeText(digits);
    if (digits.length === length) onComplete?.(digits);
  };

  const cells = Array.from({ length });
  const activeIndex = Math.min(value.length, length - 1);

  const focusRing =
    accent === 'gold' ? 'border-gold-400' : 'border-olive-500';

  return (
    <Pressable onPress={focus} accessibilityRole="none">
      <View className="flex-row justify-between gap-3">
        {cells.map((_, i) => {
          const char = value[i] ?? '';
          const isActive = (focused || !editable) && i === activeIndex;
          const filled = !!char;
          return (
            <View
              key={i}
              className={[
                'h-16 flex-1 items-center justify-center rounded-2xl border-2 bg-white',
                hasError
                  ? 'border-danger'
                  : isActive
                  ? focusRing
                  : filled
                  ? 'border-olive-200'
                  : 'border-cream-300',
              ].join(' ')}
              style={{
                shadowColor: '#000',
                shadowOpacity: isActive ? 0.06 : 0.03,
                shadowRadius: 6,
                shadowOffset: { width: 0, height: 2 },
                elevation: isActive ? 2 : 1,
              }}
            >
              <Text className="text-2xl font-extrabold text-ink">{char}</Text>
            </View>
          );
        })}
      </View>

      {editable ? (
        <TextInput
          ref={inputRef}
          value={value}
          onChangeText={handleChange}
          keyboardType="number-pad"
          autoFocus={autoFocus}
          maxLength={length}
          textContentType="oneTimeCode"
          autoComplete="sms-otp"
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          // Visually hidden but still focusable / accessible
          style={{ position: 'absolute', opacity: 0, height: 1, width: 1 }}
          accessibilityLabel={`${length}-digit code`}
          caretHidden
        />
      ) : null}
    </Pressable>
  );
}
