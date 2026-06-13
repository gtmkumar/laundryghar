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

/** Append a reply to a ticket. Optimistically merges the new message in. */
export function usePostTicketMessage(ticketId: string) {
  const qc = useQueryClient();
  return useMutation<
    Awaited<ReturnType<typeof postTicketMessage>>,
    Error,
    PostTicketMessageRequest
  >({
    mutationFn: (body) => postTicketMessage(ticketId, body),
    onSuccess: (message) => {
      qc.setQueryData<SupportTicketDetailDto>(
        supportKeys.detail(ticketId),
        (prev) =>
          prev
            ? {
                ...prev,
                ticket: { ...prev.ticket, lastMessageAt: message.createdAt },
                messages: [...prev.messages, message],
              }
            : prev,
      );
      void qc.invalidateQueries({ queryKey: supportKeys.list });
    },
  });
}

export type { SupportTicketDto };
