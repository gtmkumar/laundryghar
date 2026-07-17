import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useIsFocused } from '@react-navigation/native';
import {
  createTicket,
  getMyTickets,
  getTicketById,
  postTicketMessage,
} from '@/api/support';
import type {
  CreateSupportTicketRequest,
  PostTicketMessageRequest,
  SupportTicketDetailDto,
  SupportTicketDto,
  TicketMessageDto,
} from '@/types/api';

export const supportKeys = {
  list:   ['support', 'tickets'] as const,
  detail: (id: string) => ['support', 'ticket', id] as const,
};

/** Open tickets keep polling while the screen is focused. */
const OPEN_STATUSES = new Set(['open', 'in_progress']);

/** GET the customer's tickets (newest first). */
export function useMyTickets() {
  return useQuery({
    queryKey: supportKeys.list,
    queryFn:  getMyTickets,
    staleTime: 30_000,
  });
}

/**
 * GET a single ticket + its message thread. Polls every 20s while focused and
 * the ticket is still open/in_progress so agent replies appear without a manual
 * refresh; stops on resolved/closed.
 */
export function useTicketDetail(id: string) {
  const isFocused = useIsFocused();
  return useQuery({
    queryKey: supportKeys.detail(id),
    queryFn:  () => getTicketById(id),
    enabled:  !!id,
    refetchInterval: (query) => {
      if (!isFocused) return false;
      const status = query.state.data?.ticket.status;
      return status && OPEN_STATUSES.has(status) ? 20_000 : false;
    },
  });
}

/** Create a ticket. Seeds the detail cache and refreshes the list. */
export function useCreateTicket() {
  const qc = useQueryClient();
  return useMutation<SupportTicketDetailDto, Error, CreateSupportTicketRequest>({
    mutationFn: createTicket,
    onSuccess: (detail) => {
      qc.setQueryData(supportKeys.detail(detail.ticket.id), detail);
      void qc.invalidateQueries({ queryKey: supportKeys.list });
    },
  });
}

/**
 * Append a reply to a ticket. The message is added to the thread optimistically
 * (under a temp id) so it appears the instant the user hits send; onSuccess swaps
 * in the server message, and onError removes the temp bubble (the composer screen
 * surfaces the failure Alert).
 */
export function usePostTicketMessage(ticketId: string) {
  const qc = useQueryClient();
  return useMutation<
    Awaited<ReturnType<typeof postTicketMessage>>,
    Error,
    PostTicketMessageRequest,
    { previous?: SupportTicketDetailDto; tempId: string }
  >({
    mutationFn: (body) => postTicketMessage(ticketId, body),
    onMutate: async (body) => {
      await qc.cancelQueries({ queryKey: supportKeys.detail(ticketId) });
      const previous = qc.getQueryData<SupportTicketDetailDto>(
        supportKeys.detail(ticketId),
      );
      const now = new Date().toISOString();
      const tempId = `temp-${Date.now()}`;
      const optimistic: TicketMessageDto = {
        id: tempId,
        senderType: 'customer',
        body: body.body,
        createdAt: now,
      };
      qc.setQueryData<SupportTicketDetailDto>(
        supportKeys.detail(ticketId),
        (prev) =>
          prev
            ? {
                ...prev,
                ticket: { ...prev.ticket, lastMessageAt: now },
                messages: [...prev.messages, optimistic],
              }
            : prev,
      );
      return { previous, tempId };
    },
    onSuccess: (message, _body, ctx) => {
      qc.setQueryData<SupportTicketDetailDto>(
        supportKeys.detail(ticketId),
        (prev) =>
          prev
            ? {
                ...prev,
                ticket: { ...prev.ticket, lastMessageAt: message.createdAt },
                messages: prev.messages.map((m) =>
                  m.id === ctx?.tempId ? message : m,
                ),
              }
            : prev,
      );
    },
    // Consumer (ticket detail) surfaces the failure Alert; here we drop the temp bubble.
    onError: (_err, _body, ctx) => {
      if (ctx?.previous) {
        qc.setQueryData(supportKeys.detail(ticketId), ctx.previous);
      }
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: supportKeys.list });
    },
  });
}

export type { SupportTicketDto };
