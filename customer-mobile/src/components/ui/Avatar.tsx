import React from 'react';
import { Text, View } from 'react-native';

interface AvatarProps {
  name?: string | null;
  size?: number;
  textClassName?: string;
}

const TINTS = ['#5C6A33', '#73803F', '#4A552A', '#8A641D', '#3B4423'];

function initials(name?: string | null): string {
  if (!name) return 'U';
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

function tintFor(name?: string | null): string {
  if (!name) return TINTS[0];
  let hash = 0;
  for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) >>> 0;
  return TINTS[hash % TINTS.length];
}

export function Avatar({ name, size = 44, textClassName = 'text-base' }: AvatarProps) {
  return (
    <View
      className="items-center justify-center"
      style={{ width: size, height: size, borderRadius: size / 2, backgroundColor: tintFor(name) }}
    >
      <Text className={`font-extrabold text-white ${textClassName}`}>{initials(name)}</Text>
    </View>
  );
}
