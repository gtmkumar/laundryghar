/**
 * Client-side mirror of laundryghar.Orders OrderStateMachine.AllowedTransitions.
 * Drives which status-action buttons the drawer shows from the current status.
 * Keep in sync with:
 *   backend/laundryghar/laundryghar.Orders/Application/Common/OrderStateMachine.cs
 *
 * NOTE: `cancelled` is reached via the dedicated POST /cancel endpoint, not the
 * status PATCH. It IS listed as an allowed target in the backend map, but we
 * strip it from the PATCH action buttons here (see ADVANCEABLE_TARGETS) and
 * surface Cancel as a distinct destructive action instead.
 */

export const ORDER_STATUS = {
  placed: 'placed',
  pickup_scheduled: 'pickup_scheduled',
  pickup_assigned: 'pickup_assigned',
  pickup_in_progress: 'pickup_in_progress',
  picked_up: 'picked_up',
  received: 'received',
  sorting: 'sorting',
  in_process: 'in_process',
  qc: 'qc',
  ready: 'ready',
  delivery_scheduled: 'delivery_scheduled',
  delivery_assigned: 'delivery_assigned',
  out_for_delivery: 'out_for_delivery',
  delivered: 'delivered',
  cancelled: 'cancelled',
  returned: 'returned',
  rewash: 'rewash',
  disputed: 'disputed',
  closed: 'closed',
} as const

export type OrderStatusCode = keyof typeof ORDER_STATUS

/** All status codes in lifecycle order — drives the status filter dropdown. */
export const ORDER_STATUS_LIST: string[] = Object.values(ORDER_STATUS)

const S = ORDER_STATUS

/** Mirror of OrderStateMachine.AllowedTransitions (verbatim targets, incl. cancelled). */
export const ALLOWED_TRANSITIONS: Record<string, string[]> = {
  [S.placed]: [S.pickup_scheduled, S.cancelled, S.disputed],
  [S.pickup_scheduled]: [S.pickup_assigned, S.cancelled, S.disputed],
  [S.pickup_assigned]: [S.pickup_in_progress, S.cancelled, S.disputed],
  [S.pickup_in_progress]: [S.picked_up, S.cancelled, S.disputed],
  [S.picked_up]: [S.received, S.disputed],
  [S.received]: [S.sorting, S.disputed],
  [S.sorting]: [S.in_process, S.disputed],
  [S.in_process]: [S.qc, S.disputed],
  [S.qc]: [S.ready, S.rewash, S.disputed],
  [S.ready]: [S.delivery_scheduled, S.returned, S.disputed],
  [S.delivery_scheduled]: [S.delivery_assigned, S.returned, S.disputed],
  [S.delivery_assigned]: [S.out_for_delivery, S.returned, S.disputed],
  [S.out_for_delivery]: [S.delivered, S.returned, S.disputed],
  [S.delivered]: [S.closed, S.rewash, S.disputed],
  [S.rewash]: [S.sorting, S.disputed],
  [S.returned]: [S.closed],
  [S.disputed]: [S.closed, S.in_process],
  [S.cancelled]: [],
  [S.closed]: [],
}

/**
 * The PATCH-status targets to render as action buttons: all allowed transitions
 * minus `cancelled` (handled by the dedicated Cancel action).
 */
export function advanceableTargets(from: string): string[] {
  return (ALLOWED_TRANSITIONS[from] ?? []).filter((t) => t !== S.cancelled)
}

/** True when the order can be cancelled from this status (cancelled is an allowed target). */
export function canCancelFrom(from: string): boolean {
  return (ALLOWED_TRANSITIONS[from] ?? []).includes(S.cancelled)
}

/** Statuses where an invoice may be generated / viewed. */
const INVOICE_STATUSES = new Set<string>([S.ready, S.delivered, S.closed])
export function invoiceAvailable(status: string): boolean {
  return INVOICE_STATUSES.has(status)
}

/**
 * Title-cases a raw status code: 'pickup_in_progress' → 'Pickup In Progress'.
 * This is the i18n FALLBACK. Prefer {@link statusLabelKey} + the t() helper at
 * call sites so locale files can override; this guarantees a clean label even
 * for statuses not yet translated.
 */
export function statusLabel(status: string): string {
  return status
    .split('_')
    .filter(Boolean)
    .map((w) => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ')
}

/** i18n key for a status label, e.g. 'orders.status.pickup_in_progress'. */
export function statusLabelKey(status: string): string {
  return `orders.status.${status}`
}

/** Terminal statuses — an order in any of these is "done" (history, not active). */
export const TERMINAL_STATUSES = new Set<string>([
  S.delivered,
  S.cancelled,
  S.closed,
  S.returned,
])

/** True when the order is in a non-terminal (active board) status. */
export function isActiveStatus(status: string): boolean {
  return !TERMINAL_STATUSES.has(status)
}

type BadgeVariant = 'default' | 'secondary' | 'success' | 'warning' | 'destructive'

/** Badge color buckets for the full status set (the list page only covers a subset). */
export function statusBadgeVariant(status: string): BadgeVariant {
  switch (status) {
    case S.placed:
    case S.pickup_scheduled:
    case S.pickup_assigned:
    case S.pickup_in_progress:
      return 'warning'
    case S.ready:
    case S.delivered:
    case S.closed:
      return 'success'
    case S.cancelled:
    case S.returned:
    case S.disputed:
      return 'destructive'
    default:
      return 'default'
  }
}
