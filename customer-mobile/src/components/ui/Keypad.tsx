import React from 'react';
import { Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface KeypadProps {
  value: string;
  onChange: (next: string) => void;
  maxLength: number;
}

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', 'back'] as const;

/** Custom numeric keypad — drives an OtpInput so the system keyboard never shows. */
export function Keypad({ value, onChange, maxLength }: KeypadProps) {
  const press = (key: (typeof KEYS)[number]) => {
    if (key === 'back') {
      onChange(value.slice(0, -1));
      return;
    }
    if (key === '') return;
    if (value.length >= maxLength) return;
    onChange(value + key);
  };

  return (
    <View className="flex-row flex-wrap">
      {KEYS.map((key, i) => {
        if (key === '') {
          return <View key={i} className="h-[60px] w-1/3" />;
        }
        return (
          <View key={i} className="w-1/3 p-1.5">
            <Pressable
              onPress={() => press(key)}
              accessibilityRole="button"
              accessibilityLabel={key === 'back' ? 'Delete' : key}
              className="h-[60px] items-center justify-center rounded-2xl border border-cream-300 bg-surface-card active:bg-cream-200"
            >
              {key === 'back' ? (
                <Ionicons name="backspace-outline" size={24} color="#3C3F35" />
              ) : (
                <Text className="text-2xl font-bold text-ink">{key}</Text>
              )}
            </Pressable>
          </View>
        );
      })}
    </View>
  );
}
