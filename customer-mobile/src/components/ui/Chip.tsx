import React from 'react';
import { Pressable, Text } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

interface ChipProps {
  label: string;
  selected?: boolean;
  onPress?: () => void;
  icon?: React.ComponentProps<typeof Ionicons>['name'];
  /** Selected accent colour. */
  accent?: 'olive' | 'gold';
}

/** Pill-shaped filter / toggle chip. */
export function Chip({ label, selected = false, onPress, icon, accent = 'olive' }: ChipProps) {
  const selectedBg = accent === 'gold' ? 'bg-gold-400' : 'bg-olive-600';
  const selectedText = accent === 'gold' ? 'text-olive-900' : 'text-white';
  const iconColor = selected
    ? accent === 'gold'
      ? '#2E351C'
      : '#FFFFFF'
    : '#5C6A33';

  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityState={{ selected }}
      className={[
        'mr-2 flex-row items-center gap-1.5 rounded-full px-3.5 py-2',
        selected ? selectedBg : 'border border-cream-300 bg-white',
      ].join(' ')}
    >
      {icon ? <Ionicons name={icon} size={14} color={iconColor} /> : null}
      <Text className={`text-sm font-bold ${selected ? selectedText : 'text-ink-soft'}`}>
        {label}
      </Text>
    </Pressable>
  );
}
