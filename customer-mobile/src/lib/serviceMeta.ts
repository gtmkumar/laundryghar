import { Ionicons } from '@expo/vector-icons';

type IconName = React.ComponentProps<typeof Ionicons>['name'];

export interface ServiceMeta {
  icon: IconName;
  /** Soft tile background. */
  bg: string;
  /** Icon colour. */
  tint: string;
}

const DEFAULT: ServiceMeta = { icon: 'shirt-outline', bg: '#F1F3E8', tint: '#5C6A33' };

// Keyword → visual treatment. Matched case-insensitively against the service name.
const TABLE: Array<{ keys: string[]; meta: ServiceMeta }> = [
  { keys: ['dry clean', 'dryclean'], meta: { icon: 'shirt-outline',    bg: '#E7EFE3', tint: '#4F8A4F' } },
  { keys: ['wash', 'fold', 'laundry'], meta: { icon: 'water-outline',   bg: '#FBF4E0', tint: '#AE8123' } },
  { keys: ['steam', 'iron', 'press'], meta: { icon: 'thermometer-outline', bg: '#E3EAF1', tint: '#3F6E8C' } },
  { keys: ['shoe', 'footwear'], meta: { icon: 'footsteps-outline',     bg: '#F3E8E3', tint: '#8A641D' } },
  { keys: ['bag', 'luggage'], meta: { icon: 'bag-handle-outline',      bg: '#EFE3F1', tint: '#73509C' } },
  { keys: ['curtain', 'drape'], meta: { icon: 'browsers-outline',      bg: '#E3F1EF', tint: '#3F8C7E' } },
  { keys: ['carpet', 'rug'], meta: { icon: 'grid-outline',             bg: '#F1EEE3', tint: '#8A7A1D' } },
];

export function serviceMeta(name: string): ServiceMeta {
  const lower = name.toLowerCase();
  for (const row of TABLE) {
    if (row.keys.some((k) => lower.includes(k))) return row.meta;
  }
  return DEFAULT;
}
