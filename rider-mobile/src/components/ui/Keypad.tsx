/**
 * Keypad — custom numeric pad (mockup OTP screen).
 * Drives an externally-held string value; caller enforces max length.
 */
import React from 'react';
import { Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface KeypadProps {
  value:    string;
  onChange: (next: string) => void;
  maxLength: number;
}

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', 'back'];

export function Keypad({ value, onChange, maxLength }: KeypadProps) {
  const press = (key: string) => {
    if (key === 'back') {
      onChange(value.slice(0, -1));
    } else if (key && value.length < maxLength) {
      onChange(value + key);
    }
  };

  return (
    <View className="flex-row flex-wrap">
      {KEYS.map((key, i) => {
        if (key === '') return <View key={i} className="h-[72px] w-1/3" />;
        const isBack = key === 'back';
        return (
          <View key={i} className="w-1/3 px-1.5 py-1.5">
            <Pressable
              onPress={() => press(key)}
              accessibilityRole="button"
              accessibilityLabel={isBack ? 'Delete' : key}
              className="h-[60px] items-center justify-center rounded-2xl border border-cream-300 bg-surface-card active:bg-cream-200"
            >
              {isBack ? (
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
