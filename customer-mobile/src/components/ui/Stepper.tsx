import React from 'react';
import { Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface StepperProps {
  value: number;
  onChange: (next: number) => void;
  min?: number;
  max?: number;
  /** When value is 0, render an "+ Add" button instead of the stepper. */
  addLabel?: string;
}

/**
 * Quantity stepper. At value 0 it collapses to a single "+ Add" pill;
 * once at least 1 it expands to the −  N  + control (matches the items mockup).
 */
export function Stepper({ value, onChange, min = 0, max = 99, addLabel = '+ Add' }: StepperProps) {
  const dec = () => onChange(Math.max(min, value - 1));
  const inc = () => onChange(Math.min(max, value + 1));

  if (value <= 0) {
    return (
      <Pressable
        onPress={inc}
        accessibilityRole="button"
        accessibilityLabel="Add item"
        className="rounded-full border border-olive-300 px-4 py-2 active:opacity-70"
      >
        <Text className="text-sm font-bold text-olive-700">{addLabel}</Text>
      </Pressable>
    );
  }

  return (
    <View className="flex-row items-center gap-3 rounded-full bg-cream-100 px-1.5 py-1.5">
      <Pressable
        onPress={dec}
        accessibilityRole="button"
        accessibilityLabel="Decrease quantity"
        className="h-7 w-7 items-center justify-center rounded-full bg-white active:opacity-70"
      >
        <Ionicons name="remove" size={16} color="#4A552A" />
      </Pressable>
      <Text className="min-w-[16px] text-center text-base font-bold text-ink">{value}</Text>
      <Pressable
        onPress={inc}
        accessibilityRole="button"
        accessibilityLabel="Increase quantity"
        className="h-7 w-7 items-center justify-center rounded-full bg-olive-600 active:opacity-70"
      >
        <Ionicons name="add" size={16} color="#FFFFFF" />
      </Pressable>
    </View>
  );
}
