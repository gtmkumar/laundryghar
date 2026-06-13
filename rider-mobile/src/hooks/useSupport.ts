/**
 * useSupport — React Query wrappers for the rider support-ticket self-service
 * endpoints (helpdesk):
 *
 *   useMyTickets()      → GET  /rider/support/tickets
 *   useTicketDetail(id) → GET  /rider/support/tickets/{id}
 *   useCreateTicket()   → POST /rider/support/tickets
 *   usePostMessage(id)  → POST /rider/support/tickets/{id}/messages
 *
 * Mutations invalidate the affected queries on success so the list + thread
 * reflect the new ticket / message immediately (mirrors useEarnings).
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createTicket,
  fetchMyTickets,
  fetchTicketThread,
  postTicketMessage,
} from '@/api/support';
import { useAuthStore } from '@/store/authStore';
import type {
  CreateSupportTicketRequest,
  SupportTicketThreadDto,
  TicketMessageDto,
} from '@/types/api';

export const supportKeys = {
  tickets: () => ['rider', 'support', 'tickets'] as const,
  ticket: (id: string) => ['rider', 'support', 'tickets', id] as const,
};

/** My tickets, newest first (GET /rider/support/tickets). */
export function useMyTickets() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: supportKeys.tickets(),
    queryFn: fetchMyTickets,
    enabled: !!accessToken,
    staleTime: 15_000,
  });
}

/** One ticket plus its full message thread (GET /rider/support/tickets/{id}). */
export function useTicketDetail(id: string) {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: supportKeys.ticket(id),
    queryFn: () => fetchTicketThread(id),
    enabled: !!accessToken && !!id,
    staleTime: 10_000,
  });
}

/**
 * Open a new ticket (POST /rider/support/tickets). On success, primes the
 * detail cache with the returned thread and invalidates the list so the new
 * ticket appears at the top.
 */
export function useCreateTicket() {
  const queryClient = useQueryClient();
  return useMutation<SupportTicketThreadDto, Error, CreateSupportTicketRequest>({
    mutationFn: (input) => createTicket(input),
    onSuccess: (thread) => {
      queryClient.setQueryData(supportKeys.ticket(thread.ticket.id), thread);
      void queryClient.invalidateQueries({ queryKey: supportKeys.tickets() });
    },
  });
}

/**
 * Reply in a ticket thread (POST /rider/support/tickets/{id}/messages). On
 * success, invalidates the thread (to pull the new message + any status/
 * lastMessageAt change) and the list (lastMessageAt re-ordering).
 */
export function usePostMessage(id: string) {
  const queryClient = useQueryClient();
  return useMutation<TicketMessageDto, Error, string>({
    mutationFn: (body) => postTicketMessage(id, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: supportKeys.ticket(id) });
      void queryClient.invalidateQueries({ queryKey: supportKeys.tickets() });
    },
  });
}
