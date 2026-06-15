import { Badge } from '@/components/ui/badge'
import type {
  SupportRequesterType,
  SupportTicketPriority,
  SupportTicketStatus,
} from '@/types/api'

// ── Status ────────────────────────────────────────────────────────────────────

// Small label maps + derived option arrays co-located with the support badge
// components. Disabled individually for react-refresh rather than split out.
// eslint-disable-next-line react-refresh/only-export-components
export const STATUS_LABEL: Record<SupportTicketStatus, string> = {
  open: 'Open',
  in_progress: 'In progress',
  resolved: 'Resolved',
  closed: 'Closed',
}

// eslint-disable-next-line react-refresh/only-export-components
export const STATUS_OPTIONS: { value: SupportTicketStatus; label: string }[] = (
  Object.keys(STATUS_LABEL) as SupportTicketStatus[]
).map((value) => ({ value, label: STATUS_LABEL[value] }))

export function StatusBadge({ status }: { status: SupportTicketStatus }) {
  const variant =
    status === 'open'
      ? 'warning'
      : status === 'in_progress'
        ? 'default'
        : status === 'resolved'
          ? 'success'
          : 'secondary'
  return <Badge variant={variant}>{STATUS_LABEL[status]}</Badge>
}

// ── Priority ────────────────────────────────────────────────────────────────────

// eslint-disable-next-line react-refresh/only-export-components
export const PRIORITY_LABEL: Record<SupportTicketPriority, string> = {
  low: 'Low',
  normal: 'Normal',
  high: 'High',
}

// eslint-disable-next-line react-refresh/only-export-components
export const PRIORITY_OPTIONS: { value: SupportTicketPriority; label: string }[] = (
  Object.keys(PRIORITY_LABEL) as SupportTicketPriority[]
).map((value) => ({ value, label: PRIORITY_LABEL[value] }))

export function PriorityBadge({ priority }: { priority: SupportTicketPriority }) {
  const variant =
    priority === 'high' ? 'destructive' : priority === 'normal' ? 'secondary' : 'outline'
  return <Badge variant={variant}>{PRIORITY_LABEL[priority]}</Badge>
}

// ── Requester ───────────────────────────────────────────────────────────────────

export function RequesterBadge({ type }: { type: SupportRequesterType }) {
  return (
    <Badge variant={type === 'rider' ? 'default' : 'secondary'} className="capitalize">
      {type}
    </Badge>
  )
}
