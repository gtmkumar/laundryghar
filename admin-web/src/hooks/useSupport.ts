import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getSupportTickets,
  getSupportTicket,
  replyToTicket,
  updateTicket,
} from '@/api/support'
import type {
  SupportTicketStatus,
  SupportTicketDto,
  SupportTicketDetailDto,
  UpdateTicketPayload,
} from '@/types/api'
import { patchListItem, rollbackWithToast, snapshotAndSet } from '@/lib/optimistic'
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

/**
 * Change status / priority / assignee on a ticket. Optimistic: the changed
 * fields flip in the cached inbox list and the open ticket detail immediately
 * (only status/priority are rendered, so assignee patches are a no-op until the
 * refetch). onSettled invalidates every support cache to reconcile.
 */
export function useUpdateTicket() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateTicketPayload }) =>
      updateTicket(id, payload),
    onMutate: async ({ id, payload }) => {
      // Patch only the fields the list DTO actually carries (status, priority).
      const patch: Partial<SupportTicketDto> = {}
      if (payload.status !== undefined) patch.status = payload.status
      if (payload.priority !== undefined) patch.priority = payload.priority
      // List: bare SupportTicketDto[] under ['support','tickets',...].
      const listCtx = await patchListItem<SupportTicketDto>(qc, [['support', 'tickets']], id, patch)
      // Detail: { ticket, messages } under ['support','ticket',id] — patch the nested ticket.
      const detailCtx = await snapshotAndSet(qc, [['support', 'ticket', id]], (data) => {
        const detail = data as SupportTicketDetailDto
        if (!detail?.ticket) return data
        return { ...detail, ticket: { ...detail.ticket, ...patch } }
      })
      return {
        rollback: () => {
          listCtx.rollback()
          detailCtx.rollback()
        },
      }
    },
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidateSupport(qc),
  })
}
