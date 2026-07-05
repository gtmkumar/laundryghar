/**
 * WebMCP tool registration — exposes a small set of admin actions as
 * structured tools for in-browser AI agents.
 *
 * Experimental: requires Chrome 149+ with chrome://flags/#enable-webmcp-testing
 * (or the WebMCP origin trial). Chrome 150+ exposes the API as
 * document.modelContext; 149 shipped it on navigator.modelContext. Browsers
 * without the API silently register nothing — this module is a no-op there.
 *
 * Every tool reuses the existing api/ modules, so calls carry the signed-in
 * admin's Bearer token, X-Brand-Id header, silent 401 refresh, RBAC 403
 * handling and §8 step-up. An agent can do exactly what the logged-in user
 * can do — nothing more. Tools refuse to run when no one is signed in.
 */

import { getAdminCustomers } from '@/api/catalog'
import { getOrders, getOrderById, updateOrderStatus } from '@/api/orders'
import { useAuthStore } from '@/stores/authStore'

// ── Minimal API surface types (no lib.dom types exist for WebMCP yet) ────────

interface WebMCPTool<TInput> {
  name: string
  description: string
  inputSchema: Record<string, unknown>
  annotations?: { readOnlyHint?: boolean }
  execute: (input: TInput) => Promise<string>
}

interface ModelContext {
  registerTool<TInput>(tool: WebMCPTool<TInput>): Promise<void>
}

function getModelContext(): ModelContext | undefined {
  return (
    (document as Document & { modelContext?: ModelContext }).modelContext ??
    (navigator as Navigator & { modelContext?: ModelContext }).modelContext
  )
}

/** Minimal navigation surface of the react-router instance created in App.tsx. */
export interface WebMCPRouter {
  navigate: (to: string) => void | Promise<void>
}

// ── Helpers ───────────────────────────────────────────────────────────────────

const NOT_SIGNED_IN =
  'Error: no admin is signed in. Ask the user to log in to admin-web first.'

function isSignedIn(): boolean {
  return useAuthStore.getState().accessToken !== null
}

/** Wraps a tool body with the auth guard and error-to-string conversion. */
function guarded<TInput>(
  body: (input: TInput) => Promise<string>,
): (input: TInput) => Promise<string> {
  return async (input) => {
    if (!isSignedIn()) return NOT_SIGNED_IN
    try {
      return await body(input)
    } catch (err) {
      return `Error: ${err instanceof Error ? err.message : String(err)}`
    }
  }
}

// ── Tool registration ─────────────────────────────────────────────────────────

let registered = false

/**
 * Registers admin-web's WebMCP tools. Call once at startup (App.tsx). Safe to
 * call in any browser — does nothing when the WebMCP API is unavailable.
 */
export function initWebMCP(router: WebMCPRouter): void {
  const modelContext = getModelContext()
  if (!modelContext || registered) return
  registered = true

  void Promise.all([
    modelContext.registerTool<{ search?: string; status?: string; page?: number; pageSize?: number }>({
      name: 'search_customers',
      description:
        'Search the brand\'s customers by name, phone or customer code. Returns a JSON list with id, code, name, phone, lifetime orders/spend, wallet balance and status.',
      inputSchema: {
        type: 'object',
        properties: {
          search: { type: 'string', description: 'Free-text search: name, phone or customer code.' },
          status: { type: 'string', description: 'Optional status filter, e.g. "active".' },
          page: { type: 'integer', minimum: 1 },
          pageSize: { type: 'integer', minimum: 1, maximum: 100 },
        },
      },
      annotations: { readOnlyHint: true },
      execute: guarded(async ({ search, status, page, pageSize }) => {
        const result = await getAdminCustomers({ search, status, page, pageSize: pageSize ?? 20 })
        const rows = result.list.map((c) => ({
          id: c.id,
          code: c.customerCode,
          name: c.displayName ?? ([c.firstName, c.lastName].filter(Boolean).join(' ') || null),
          phone: c.phoneE164,
          email: c.email,
          lifetimeOrders: c.lifetimeOrders,
          lifetimeSpend: c.lifetimeSpend,
          walletBalance: c.walletBalance,
          segment: c.customerSegment,
          status: c.status,
        }))
        return JSON.stringify({ totalCount: result.totalCount, customers: rows })
      }),
    }),

    modelContext.registerTool<{
      status?: string
      statusGroup?: 'active' | 'history'
      dateFrom?: string
      dateTo?: string
      page?: number
      pageSize?: number
    }>({
      name: 'list_orders',
      description:
        'List the brand\'s orders, optionally filtered by status, active/history group, or created-date range (ISO dates). Returns a JSON list with id, order number, status, payment status, totals and creation time.',
      inputSchema: {
        type: 'object',
        properties: {
          status: { type: 'string', description: 'Exact order status filter.' },
          statusGroup: {
            type: 'string',
            enum: ['active', 'history'],
            description: '"active" = in-flight orders, "history" = completed/cancelled. Ignored when status is set.',
          },
          dateFrom: { type: 'string', description: 'ISO date lower bound, e.g. "2026-07-01".' },
          dateTo: { type: 'string', description: 'ISO date upper bound.' },
          page: { type: 'integer', minimum: 1 },
          pageSize: { type: 'integer', minimum: 1, maximum: 100 },
        },
      },
      annotations: { readOnlyHint: true },
      execute: guarded(async ({ status, statusGroup, dateFrom, dateTo, page, pageSize }) => {
        const result = await getOrders({
          status,
          statusGroup,
          dateFrom,
          dateTo,
          page,
          pageSize: pageSize ?? 20,
        })
        const rows = result.list.map((o) => ({
          id: o.id,
          orderNumber: o.orderNumber,
          status: o.status,
          paymentStatus: o.paymentStatus,
          totalItems: o.totalItems,
          grandTotal: o.grandTotal,
          amountDue: o.amountDue,
          currency: o.currencyCode,
          isExpress: o.isExpress,
          createdAt: o.createdAt,
        }))
        return JSON.stringify({ totalCount: result.totalCount, orders: rows })
      }),
    }),

    modelContext.registerTool<{ orderId: string }>({
      name: 'get_order',
      description:
        'Get the full details of one order by its id (use list_orders to find ids). Returns the complete order JSON including amounts, taxes and customer/store references.',
      inputSchema: {
        type: 'object',
        properties: {
          orderId: { type: 'string', description: 'The order id (UUID), not the order number.' },
        },
        required: ['orderId'],
      },
      annotations: { readOnlyHint: true },
      execute: guarded(async ({ orderId }) => JSON.stringify(await getOrderById(orderId))),
    }),

    modelContext.registerTool<{
      orderId: string
      toStatus: string
      reason?: string
      notes?: string
      customerNotified?: boolean
    }>({
      name: 'update_order_status',
      description:
        'Move an order to a new status (same action as the status button in the Orders screen — subject to the signed-in admin\'s permissions and valid status transitions). Confirm with the user before calling this.',
      inputSchema: {
        type: 'object',
        properties: {
          orderId: { type: 'string', description: 'The order id (UUID).' },
          toStatus: { type: 'string', description: 'Target status. Invalid transitions are rejected by the server.' },
          reason: { type: 'string' },
          notes: { type: 'string' },
          customerNotified: { type: 'boolean', description: 'Whether the customer should be notified. Defaults to false.' },
        },
        required: ['orderId', 'toStatus'],
      },
      execute: guarded(async ({ orderId, toStatus, reason, notes, customerNotified }) => {
        const order = await updateOrderStatus(orderId, {
          toStatus,
          reason,
          notes,
          customerNotified: customerNotified ?? false,
        })
        return `Order ${order.orderNumber} is now "${order.status}".`
      }),
    }),

    modelContext.registerTool<{ path: string }>({
      name: 'open_admin_page',
      description:
        'Navigate admin-web to an app route so the user can see it. Common routes: /orders, /customers, /items, /riders, /finance, /settings, /orders/{orderId}.',
      inputSchema: {
        type: 'object',
        properties: {
          path: { type: 'string', description: 'App-relative path starting with "/".' },
        },
        required: ['path'],
      },
      execute: guarded(async (input) => {
        if (!input.path.startsWith('/')) return 'Error: path must start with "/".'
        await router.navigate(input.path)
        return `Navigated to ${input.path}`
      }),
    }),
  ]).catch(() => {
    // Registration failure (e.g. flag half-enabled) must never break the app.
    registered = false
  })
}
