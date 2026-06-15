import { useRef, useState } from 'react'
import { LifeBuoy, Loader2, Send, ExternalLink } from 'lucide-react'
import { Link } from 'react-router-dom'
import {
  useSupportTicket,
  useReplyTicket,
  useUpdateTicket,
} from '@/hooks/useSupport'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import { FormDrawer, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
import { cn, formatDateTime } from '@/lib/utils'
import type {
  SupportTicketDto,
  SupportTicketStatus,
  SupportTicketPriority,
  TicketMessageDto,
  TicketSenderType,
} from '@/types/api'
import {
  RequesterBadge,
  STATUS_OPTIONS,
  PRIORITY_OPTIONS,
} from './supportShared'

interface Props {
  /** The row's ticket, used for an instant header while the thread loads. */
  ticket: SupportTicketDto | null
  open: boolean
  onClose: () => void
}

export function TicketDetailDrawer({ ticket, open, onClose }: Props) {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('support.manage')

  const id = open ? ticket?.id ?? null : null
  const { data, isLoading, isError, refetch } = useSupportTicket(id)

  // Prefer the freshly-loaded ticket (post-PATCH it carries the new status), but
  // fall back to the row's snapshot so the header never flickers empty.
  const header = data?.ticket ?? ticket
  const messages = data?.messages ?? []

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={LifeBuoy}
      eyebrow={header ? `Ticket ${header.ticketNumber}` : 'Support ticket'}
      title={header?.subject ?? 'Support ticket'}
      width="lg"
      footer={null}
    >
      {!header ? null : isLoading ? (
        <div className="flex items-center justify-center py-24 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading conversation…
        </div>
      ) : isError ? (
        <div className="space-y-3 py-20 text-center">
          <p className="text-sm text-red-600">Couldn’t load this ticket.</p>
          <button
            type="button"
            onClick={() => void refetch()}
            className="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
          >
            Retry
          </button>
        </div>
      ) : (
        <div className="space-y-6">
          {/* Header facts */}
          <DetailSection title="Details">
            <DetailRow
              label="Requester"
              value={
                <span className="inline-flex items-center gap-2">
                  {header.requesterName ?? '—'}
                  <RequesterBadge type={header.requesterType} />
                </span>
              }
            />
            <DetailRow label="Category" value={<span className="capitalize">{header.category}</span>} />
            <DetailRow label="Opened" value={formatDateTime(header.createdAt)} />
            <DetailRow label="Last message" value={formatDateTime(header.lastMessageAt)} />
            {header.orderId && (
              <DetailRow
                label="Order"
                value={
                  <Link
                    to={`/orders?id=${header.orderId}`}
                    className="inline-flex items-center gap-1 font-medium text-lg-green hover:underline"
                  >
                    View order <ExternalLink className="h-3.5 w-3.5" />
                  </Link>
                }
              />
            )}
          </DetailSection>

          {/* Status + priority controls (gated by support.manage) */}
          <TicketControls ticket={header} canManage={canManage} />

          {/* Conversation thread */}
          <section className="space-y-3">
            <h3 className="text-sm font-semibold text-gray-900">
              Conversation{messages.length > 0 ? ` · ${messages.length}` : ''}
            </h3>
            <MessageThread messages={messages} />
          </section>

          {/* Reply composer */}
          {canManage ? (
            <ReplyComposer ticketId={header.id} />
          ) : (
            <p className="rounded-xl border border-dashed border-gray-200 bg-gray-50 px-4 py-3 text-center text-xs text-gray-400">
              You have read-only access to this ticket.
            </p>
          )}
        </div>
      )}
    </FormDrawer>
  )
}

// ── Status + priority controls ──────────────────────────────────────────────────

function TicketControls({
  ticket,
  canManage,
}: {
  ticket: SupportTicketDto
  canManage: boolean
}) {
  const update = useUpdateTicket()
  const [pending, setPending] = useState<'status' | 'priority' | null>(null)

  const setStatus = async (status: SupportTicketStatus) => {
    if (status === ticket.status) return
    setPending('status')
    try {
      await update.mutateAsync({ id: ticket.id, payload: { status } })
      showToast('success', 'Ticket status updated.')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not update status.')
    } finally {
      setPending(null)
    }
  }

  const setPriority = async (priority: SupportTicketPriority) => {
    if (priority === ticket.priority) return
    setPending('priority')
    try {
      await update.mutateAsync({ id: ticket.id, payload: { priority } })
      showToast('success', 'Ticket priority updated.')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not update priority.')
    } finally {
      setPending(null)
    }
  }

  const selectClass =
    'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-900 outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15 disabled:opacity-60'

  return (
    <div className="grid grid-cols-2 gap-3">
      <label className="space-y-1.5">
        <span className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-gray-500">
          Status
          {pending === 'status' && <Loader2 className="h-3 w-3 animate-spin" />}
        </span>
        <select
          value={ticket.status}
          disabled={!canManage || pending !== null}
          onChange={(e) => void setStatus(e.target.value as SupportTicketStatus)}
          className={selectClass}
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </label>

      <label className="space-y-1.5">
        <span className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-gray-500">
          Priority
          {pending === 'priority' && <Loader2 className="h-3 w-3 animate-spin" />}
        </span>
        <select
          value={ticket.priority}
          disabled={!canManage || pending !== null}
          onChange={(e) => void setPriority(e.target.value as SupportTicketPriority)}
          className={selectClass}
        >
          {PRIORITY_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}

// ── Message thread ──────────────────────────────────────────────────────────────

const SENDER_LABEL: Record<TicketSenderType, string> = {
  agent: 'Support agent',
  customer: 'Customer',
  rider: 'Rider',
  system: 'System',
}

function MessageThread({ messages }: { messages: TicketMessageDto[] }) {
  if (messages.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-gray-200 bg-white py-10 text-center text-sm text-gray-400">
        No messages on this ticket yet.
      </div>
    )
  }
  return (
    <div className="space-y-3">
      {messages.map((m) => (
        <MessageBubble key={m.id} message={m} />
      ))}
    </div>
  )
}

function MessageBubble({ message }: { message: TicketMessageDto }) {
  const isAgent = message.senderType === 'agent'
  const isSystem = message.senderType === 'system'

  if (isSystem) {
    return (
      <p className="text-center text-xs text-gray-400">
        {message.body} · {formatDateTime(message.createdAt)}
      </p>
    )
  }

  return (
    <div className={cn('flex', isAgent ? 'justify-end' : 'justify-start')}>
      <div className={cn('max-w-[85%] space-y-1', isAgent ? 'items-end text-right' : 'items-start')}>
        <span className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">
          {SENDER_LABEL[message.senderType]}
        </span>
        <div
          className={cn(
            'whitespace-pre-wrap break-words rounded-2xl px-4 py-2.5 text-sm',
            isAgent
              ? 'bg-lg-green text-white'
              : 'border border-gray-200 bg-white text-gray-800',
          )}
        >
          {message.body}
        </div>
        <span className="block text-[11px] text-gray-400">{formatDateTime(message.createdAt)}</span>
      </div>
    </div>
  )
}

// ── Reply composer ──────────────────────────────────────────────────────────────

function ReplyComposer({ ticketId }: { ticketId: string }) {
  const reply = useReplyTicket()
  const [body, setBody] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  // Reset the draft when switching tickets in the same drawer mount.
  const [draftTicketId, setDraftTicketId] = useState(ticketId)
  if (draftTicketId !== ticketId) {
    setDraftTicketId(ticketId)
    setBody('')
  }

  const send = async () => {
    const trimmed = body.trim()
    if (!trimmed) {
      showToast('error', 'Write a reply before sending.')
      return
    }
    try {
      await reply.mutateAsync({ id: ticketId, body: trimmed })
      setBody('')
      textareaRef.current?.focus()
      showToast('success', 'Reply sent.')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not send reply.')
    }
  }

  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-900">Reply</h3>
      <textarea
        ref={textareaRef}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        onKeyDown={(e) => {
          // Cmd/Ctrl+Enter sends, matching common inbox conventions.
          if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
            e.preventDefault()
            void send()
          }
        }}
        rows={4}
        placeholder="Type your reply to the customer or rider…"
        className="w-full resize-y rounded-xl border border-gray-200 bg-white px-3 py-2.5 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
      />
      <div className="flex items-center justify-between">
        <span className="text-xs text-gray-400">Sending moves an open ticket to “In progress”.</span>
        <button
          type="button"
          onClick={() => void send()}
          disabled={reply.isPending || !body.trim()}
          className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
        >
          {reply.isPending ? (
            <Loader2 className="h-4 w-4 animate-spin" />
          ) : (
            <Send className="h-4 w-4" />
          )}
          Send reply
        </button>
      </div>
    </section>
  )
}
