import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSupportTickets,
  getSupportTicket,
  replyToTicket,
  updateTicket,
} from '@/api/support'
import type { SupportTicketStatus, UpdateTicketPayload } from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

/**
 * Support tickets for the chosen workflow status (defaults to 'open' at the call
 * site). The backend returns one status bucket at a time, so the filter drives
 * the query rather than an in-memory FilterableTable filter.
 */
export function useSupportTickets(status?: SupportTicketStatus) {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['support', 'tickets', brandId, status ?? ''],
    queryFn: () => getSupportTickets(status),
    enabled: !!brandId,
  })
}

/** A single ticket + its message thread. Disabled until an id is selected. */
export function useSupportTicket(id: string | null) {
  return useQuery({
    queryKey: ['support', 'ticket', id],
    queryFn: () => getSupportTicket(id!),
    enabled: !!id,
  })
}

/** Invalidate every support cache so the list bucket and the open thread refresh. */
function invalidateSupport(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ['support'] })
}

/** Post an agent reply, then refresh the thread + the inbox (status may flip). */
export function useReplyTicket() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => replyToTicket(id, body),
    onSuccess: () => invalidateSupport(qc),
  })
}

/** Change status / priority / assignee on a ticket, then refresh everything. */
export function useUpdateTicket() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateTicketPayload }) =>
      updateTicket(id, payload),
    onSuccess: () => invalidateSupport(qc),
  })
}
