/**
 * Rider support-ticket self-service API — maps to the LIVE rider helpdesk
 * endpoints:
 *
 *   POST /api/v1/rider/support/tickets
 *     body { subject, message, category?, orderId? }
 *     → SingleResponse<{ ticket, messages }>
 *
 *   GET  /api/v1/rider/support/tickets
 *     → ListResponse<SupportTicketDto>  (newest first)
 *
 *   GET  /api/v1/rider/support/tickets/{id}
 *     → SingleResponse<{ ticket, messages }>
 *
 *   POST /api/v1/rider/support/tickets/{id}/messages
 *     body { body }
 *     → SingleResponse<TicketMessageDto>
 *
 * Endpoint prefix: {Logistics}/api/v1/rider/ — RiderOnly policy (Bearer token,
 * user_type=rider). The rider identity is resolved from the JWT, never the path.
 *
 * Mirrors the documents / earnings API modules: typed axios calls through
 * logisticsClient, the {status,data} envelope unwrapped via unwrapSingle /
 * unwrapList, and errors normalised to ApiError carrying the server message.
 */
import axios from 'axios';
import { ApiError, logisticsClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  CreateSupportTicketRequest,
  ListResponse,
  SingleResponse,
  SupportTicketDto,
  SupportTicketThreadDto,
  TicketMessageDto,
} from '@/types/api';

function toApiError(e: unknown, fallback: string): ApiError {
  if (axios.isAxiosError(e) && e.response?.data) {
    const env = e.response.data as SingleResponse<unknown>;
    return new ApiError(env.message?.responseMessage ?? fallback, { status: false });
  }
  if (e instanceof ApiError) return e;
  return new ApiError(fallback);
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/support/tickets — my tickets, newest first.
// ---------------------------------------------------------------------------
export async function fetchMyTickets(): Promise<SupportTicketDto[]> {
  try {
    const res = await logisticsClient.get<ListResponse<SupportTicketDto>>(
      '/rider/support/tickets',
    );
    return unwrapList(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load your support tickets. Try again.');
  }
}

// ---------------------------------------------------------------------------
// GET /api/v1/rider/support/tickets/{id} — one ticket + its full thread.
// ---------------------------------------------------------------------------
export async function fetchTicketThread(id: string): Promise<SupportTicketThreadDto> {
  try {
    const res = await logisticsClient.get<SingleResponse<SupportTicketThreadDto>>(
      `/rider/support/tickets/${id}`,
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not load this ticket. Try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/rider/support/tickets — open a new ticket with its first message.
// Returns the created ticket plus the seeded message(s).
// ---------------------------------------------------------------------------
export async function createTicket(
  input: CreateSupportTicketRequest,
): Promise<SupportTicketThreadDto> {
  try {
    const res = await logisticsClient.post<SingleResponse<SupportTicketThreadDto>>(
      '/rider/support/tickets',
      input,
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not open your ticket. Try again.');
  }
}

// ---------------------------------------------------------------------------
// POST /api/v1/rider/support/tickets/{id}/messages — reply in a thread.
// ---------------------------------------------------------------------------
export async function postTicketMessage(
  id: string,
  body: string,
): Promise<TicketMessageDto> {
  try {
    const res = await logisticsClient.post<SingleResponse<TicketMessageDto>>(
      `/rider/support/tickets/${id}/messages`,
      { body },
    );
    return unwrapSingle(res.data);
  } catch (e) {
    throw toApiError(e, 'Could not send your message. Try again.');
  }
}
