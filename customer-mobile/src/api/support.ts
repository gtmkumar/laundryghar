/**
 * Support tickets API — maps to the "Customer - Support" endpoints in the
 * Orders service.  Endpoint prefix: {Orders}/api/v1/customer/support/tickets
 */
import { ordersClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  CreateSupportTicketRequest,
  ListResponse,
  PostTicketMessageRequest,
  SingleResponse,
  SupportTicketDetailDto,
  SupportTicketDto,
  TicketMessageDto,
} from '@/types/api';

/** GET /api/v1/customer/support/tickets — newest first. */
export async function getMyTickets(): Promise<SupportTicketDto[]> {
  const res = await ordersClient.get<ListResponse<SupportTicketDto>>(
    '/customer/support/tickets/',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/customer/support/tickets/{id} — ticket + full message thread. */
export async function getTicketById(
  id: string,
): Promise<SupportTicketDetailDto> {
  const res = await ordersClient.get<SingleResponse<SupportTicketDetailDto>>(
    `/customer/support/tickets/${id}`,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/support/tickets — opens a ticket with the first message. */
export async function createTicket(
  body: CreateSupportTicketRequest,
): Promise<SupportTicketDetailDto> {
  const res = await ordersClient.post<SingleResponse<SupportTicketDetailDto>>(
    '/customer/support/tickets/',
    body,
  );
  return unwrapSingle(res.data);
}

/** POST /api/v1/customer/support/tickets/{id}/messages — appends a reply. */
export async function postTicketMessage(
  id: string,
  body: PostTicketMessageRequest,
): Promise<TicketMessageDto> {
  const res = await ordersClient.post<SingleResponse<TicketMessageDto>>(
    `/customer/support/tickets/${id}/messages`,
    body,
  );
  return unwrapSingle(res.data);
}
