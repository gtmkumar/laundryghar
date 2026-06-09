import React from 'react';
import { Text, View } from 'react-native';

type Tone = 'olive' | 'gold' | 'neutral' | 'success' | 'danger' | 'info';

interface BadgeProps {
  label: string;
  tone?: Tone;
  className?: string;
}

const TONES: Record<Tone, { bg: string; text: string }> = {
  olive:   { bg: 'bg-olive-100', text: 'text-olive-800' },
  gold:    { bg: 'bg-gold-100',  text: 'text-gold-700' },
  neutral: { bg: 'bg-cream-200', text: 'text-ink-soft' },
  success: { bg: 'bg-olive-100', text: 'text-success' },
  danger:  { bg: 'bg-red-100',   text: 'text-danger' },
  info:    { bg: 'bg-blue-50',   text: 'text-info' },
};

export function Badge({ label, tone = 'neutral', className = '' }: BadgeProps) {
  const t = TONES[tone];
  return (
    <View className={`self-start rounded-full px-2.5 py-1 ${t.bg} ${className}`}>
      <Text className={`text-[11px] font-bold ${t.text}`}>{label}</Text>
    </View>
  );
}
