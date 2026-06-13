/**
 * Shared presentation helpers for support tickets — status → Badge tone +
 * i18n label key, and the category options for the new-ticket form.
 */
import type { SupportTicketStatus } from '@/types/api';

type BadgeTone = 'olive' | 'gold' | 'neutral' | 'success' | 'danger' | 'info';

/** Map a ticket status to a Badge tone. */
export function statusTone(status: SupportTicketStatus): BadgeTone {
  switch (status) {
    case 'open':
      return 'gold';
    case 'in_progress':
      return 'info';
    case 'resolved':
      return 'success';
    case 'closed':
      return 'neutral';
    default:
      return 'neutral';
  }
}

/** i18n key for a ticket status label. */
export function statusLabelKey(status: SupportTicketStatus): string {
  switch (status) {
    case 'open':
      return 'support.statusOpen';
    case 'in_progress':
      return 'support.statusInProgress';
    case 'resolved':
      return 'support.statusResolved';
    case 'closed':
      return 'support.statusClosed';
    default:
      return 'support.statusOpen';
  }
}

/** Category options surfaced in the new-ticket form. `value` is sent to the API. */
export const SUPPORT_CATEGORIES: { value: string; labelKey: string }[] = [
  { value: 'general',  labelKey: 'support.categoryGeneral' },
  { value: 'order',    labelKey: 'support.categoryOrder' },
  { value: 'payment',  labelKey: 'support.categoryPayment' },
  { value: 'delivery', labelKey: 'support.categoryDelivery' },
  { value: 'damage',   labelKey: 'support.categoryDamage' },
];
