/**
 * Order Detail — shows full order info + status advance buttons.
 * Status transitions are PATCH /api/v1/admin/orders/{id}/status.
 */
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft, Loader2, Printer, FileText, Tag } from 'lucide-react'
import { useOrder, useUpdateOrderStatus, useOpenInvoicePdf } from '@/hooks/useOrders'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ReceiptSlip } from '@/components/print/Receipt'
import { GarmentTags } from '@/components/print/GarmentTags'
import { usePosStore } from '@/stores/posStore'
import {
  formatCurrency,
  formatDateTime,
  orderStatusColor,
  nextStatuses,
} from '@/lib/utils'
import { useState } from 'react'

export function OrderDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { activeStore } = usePosStore()
  const [statusError, setStatusError] = useState<string | null>(null)
  const [printMode, setPrintMode] = useState<'receipt' | 'tags' | null>(null)

  const { data: order, isLoading, isError, refetch } = useOrder(id ?? '')
  const { mutate: updateStatus, isPending } = useUpdateOrderStatus()
  const { mutate: openInvoice, isPending: invoicePending, isError: invoiceError } =
    useOpenInvoicePdf()

  if (!id) return null
  if (isLoading) return <LoadingState message="Loading order…" />
  if (isError || !order) {
    return <ErrorState message="Order not found." onRetry={() => refetch()} />
  }

  const allowedNext = nextStatuses(order.status)

  function handlePrint(mode: 'receipt' | 'tags') {
    setPrintMode(mode)
    setTimeout(() => {
      window.print()
      setPrintMode(null)
    }, 50)
  }

  function handleStatusChange(toStatus: string) {
    setStatusError(null)
    updateStatus(
      {
        id: order!.id,
        payload: { toStatus, customerNotified: false, notes: null, reason: null },
      },
      {
        onError: (err) => {
          setStatusError(err instanceof Error ? err.message : 'Status update failed.')
        },
      },
    )
  }

  return (
    <>
    <div className="p-4 lg:p-6 space-y-5 max-w-2xl mx-auto no-print">
      {/* Back */}
      <button
        type="button"
        onClick={() => navigate('/orders')}
        className="flex items-center gap-2 text-sm text-gray-500 hover:text-gray-800 mb-2"
      >
        <ArrowLeft className="h-4 w-4" />
        Back to orders
      </button>

      {/* Header */}
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-gray-900">{order.orderNumber}</h1>
          <p className="text-sm text-gray-500 mt-0.5">{formatDateTime(order.placedAt)}</p>
        </div>
        <div className="flex flex-col items-end gap-2">
          <span
            className={`px-3 py-1 rounded-full text-sm font-semibold ${orderStatusColor(order.status)}`}
          >
            {order.status.replace(/_/g, ' ')}
          </span>
          {order.isExpress && (
            <Badge variant="warning">Express</Badge>
          )}
        </div>
      </div>

      {/* Status advance buttons */}
      {allowedNext.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Advance Status</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-3">
            {statusError && (
              <p className="w-full text-xs text-red-600">{statusError}</p>
            )}
            {allowedNext.map((status) => (
              <Button
                key={status}
                size="touch"
                variant={status === 'cancelled' ? 'destructive' : 'default'}
                disabled={isPending}
                onClick={() => handleStatusChange(status)}
                className="capitalize"
              >
                {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
                {status.replace(/_/g, ' ')}
              </Button>
            ))}
          </CardContent>
        </Card>
      )}

      {/* Documents & printing */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Documents</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap gap-3">
            <Button size="touch" variant="outline" onClick={() => handlePrint('receipt')}>
              <Printer className="h-5 w-5" /> Receipt
            </Button>
            <Button size="touch" variant="outline" onClick={() => handlePrint('tags')}>
              <Tag className="h-5 w-5" /> Garment Tags
            </Button>
            <Button
              size="touch"
              variant="outline"
              disabled={invoicePending}
              onClick={() => openInvoice(order.id)}
            >
              {invoicePending ? (
                <Loader2 className="h-5 w-5 animate-spin" />
              ) : (
                <FileText className="h-5 w-5" />
              )}
              Tax Invoice PDF
            </Button>
          </div>
          {invoiceError && (
            <p className="text-xs text-amber-600">
              Invoice isn't available yet. A GST tax invoice can only be generated
              once the order reaches <strong>ready</strong>, <strong>delivered</strong>,
              or <strong>closed</strong>. Use the receipt slip for the counter
              hand-off in the meantime.
            </p>
          )}
        </CardContent>
      </Card>

      {/* Totals */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Totals</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          <div className="flex justify-between">
            <span className="text-gray-500">Subtotal</span>
            <span>{formatCurrency(order.subtotal, order.currencyCode)}</span>
          </div>
          {order.addonTotal > 0 && (
            <div className="flex justify-between">
              <span className="text-gray-500">Add-ons</span>
              <span>{formatCurrency(order.addonTotal, order.currencyCode)}</span>
            </div>
          )}
          {order.expressSurcharge > 0 && (
            <div className="flex justify-between">
              <span className="text-gray-500">Express surcharge</span>
              <span>{formatCurrency(order.expressSurcharge, order.currencyCode)}</span>
            </div>
          )}
          <div className="flex justify-between">
            <span className="text-gray-500">Tax (GST)</span>
            <span>{formatCurrency(order.taxTotal, order.currencyCode)}</span>
          </div>
          <div className="border-t border-gray-100 pt-2 flex justify-between font-bold text-base">
            <span>Grand Total</span>
            <span className="text-blue-700">{formatCurrency(order.grandTotal, order.currencyCode)}</span>
          </div>
          {(order.amountDue ?? 0) > 0 && (
            <div className="flex justify-between text-red-700 font-medium">
              <span>Amount Due</span>
              <span>{formatCurrency(order.amountDue ?? 0, order.currencyCode)}</span>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Line items */}
      {order.items && order.items.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Line Items</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {order.items.map((item) => (
              <div key={item.id} className="flex items-start justify-between text-sm gap-3">
                <div className="flex-1 min-w-0">
                  <p className="font-medium text-gray-900">{item.itemNameSnapshot}</p>
                  <p className="text-xs text-gray-500">{item.serviceNameSnapshot}</p>
                </div>
                <div className="text-right shrink-0">
                  <p className="font-medium">{formatCurrency(item.lineTotal, order.currencyCode)}</p>
                  <p className="text-xs text-gray-400">
                    {item.quantity} × {formatCurrency(item.unitPrice, order.currencyCode)}
                  </p>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      {/* Status history */}
      {order.statusHistory && order.statusHistory.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Status History</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {order.statusHistory.map((h) => (
              <div key={h.id} className="flex items-start justify-between text-sm gap-2">
                <div>
                  {h.fromStatus && (
                    <span className="text-gray-400 capitalize">
                      {h.fromStatus.replace(/_/g, ' ')} →{' '}
                    </span>
                  )}
                  <span className="font-medium capitalize text-gray-800">
                    {h.toStatus.replace(/_/g, ' ')}
                  </span>
                  {h.reason && <p className="text-xs text-gray-400 mt-0.5">{h.reason}</p>}
                </div>
                <span className="text-xs text-gray-400 shrink-0">
                  {formatDateTime(h.changedAt)}
                </span>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>

    {/* Print payloads — visible only during window.print (see @media print). */}
    {printMode === 'receipt' && (
      <ReceiptSlip
        order={order}
        storeName={activeStore?.name ?? 'Laundry Ghar'}
        storeCode={activeStore?.code}
        amountPaid={order.amountPaid > 0 ? order.amountPaid : null}
      />
    )}
    {printMode === 'tags' && (
      <GarmentTags order={order} storeCode={activeStore?.code} />
    )}
    </>
  )
}
