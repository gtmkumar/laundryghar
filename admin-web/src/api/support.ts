import { logisticsClient, unwrap } from './client'
import type {
  ApiResponse,
  SupportTicketDto,
  SupportTicketDetailDto,
  SupportTicketStatus,
  TicketMessageDto,
  UpdateTicketPayload,
} from '@/types/api'

// The admin support inbox lives in laundryghar.Operations (Logistics admin group),
// so it is reached through the logistics client — same as rider payouts/incentives.
const TICKETS = '/api/v1/admin/support/tickets'

/**
 * Support tickets, newest by lastMessageAt. Pass a status to narrow to one
 * workflow bucket; omit it to fetch every ticket.
 */
export async function getSupportTickets(
  status?: SupportTicketStatus,
): Promise<SupportTicketDto[]> {
  const { data } = await logisticsClient.get<ApiResponse<SupportTicketDto[]>>(TICKETS, {
    params: status ? { status } : undefined,
  })
  return unwrap(data) ?? []
}

/** A single ticket plus its full threaded conversation (oldest → newest). */
export async function getSupportTicket(id: string): Promise<SupportTicketDetailDto> {
  const { data } = await logisticsClient.get<ApiResponse<SupportTicketDetailDto>>(
    `${TICKETS}/${id}`,
  )
  return unwrap(data)
}

/** Post an agent reply. Server flips an 'open' ticket to 'in_progress'. */
export async function replyToTicket(id: string, body: string): Promise<TicketMessageDto> {
  const { data } = await logisticsClient.post<ApiResponse<TicketMessageDto>>(
    `${TICKETS}/${id}/messages`,
    { body },
  )
  return unwrap(data)
}

/** Patch status / priority / assignee. Only the supplied fields change. */
export async function updateTicket(
  id: string,
  payload: UpdateTicketPayload,
): Promise<SupportTicketDto> {
  const { data } = await logisticsClient.patch<ApiResponse<SupportTicketDto>>(
    `${TICKETS}/${id}`,
    payload,
  )
  return unwrap(data)
}
