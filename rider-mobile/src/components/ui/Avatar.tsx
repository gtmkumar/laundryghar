/**
 * Avatar — initials circle. Derives up-to-2 initials from a name/code and
 * picks a deterministic olive tint so the same person always looks the same.
 */
import React from 'react';
import { Text, View } from 'react-native';

interface AvatarProps {
  name?:  string | null;
  size?:  number;
  /** Tailwind text size class for the initials. */
  textClassName?: string;
}

function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

const TINTS = ['#5C6A33', '#73803F', '#4A552A', '#8A641D', '#3B4423'];

export function Avatar({ name, size = 44, textClassName = 'text-base' }: AvatarProps) {
  const label = name?.trim() || 'Rider';
  const initials = initialsOf(label);
  const tint = TINTS[label.charCodeAt(0) % TINTS.length];

  return (
    <View
      accessibilityRole="image"
      accessibilityLabel={label}
      style={{ width: size, height: size, borderRadius: size / 2, backgroundColor: tint }}
      className="items-center justify-center"
    >
      <Text className={`font-extrabold text-white ${textClassName}`}>{initials}</Text>
    </View>
  );
}
