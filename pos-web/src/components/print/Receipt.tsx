/**
 * Counter receipt — browser-print HTML slip for the immediate walk-in hand-off.
 *
 * Why HTML print and not the invoice PDF: backend invoice generation is gated to
 * billable statuses (ready | delivered | closed). A fresh walk-in order is
 * `placed`, so no GST invoice exists yet. This slip is the counter
 * acknowledgement; it explicitly states the tax invoice follows on delivery.
 *
 * Rendering: the `.print-area` element is shown only during `window.print()`
 * (see the @media print rules in index.css); everything else is hidden.
 */
import { formatCurrency, formatDateTime } from '@/lib/utils'
import type { OrderDto } from '@/types/api'

interface ReceiptProps {
  order: OrderDto
  storeName: string
  storeCode?: string
  customerLabel?: string | null
  /** Amount actually booked against the order (capped at the balance). */
  amountPaid?: number | null
  /**
   * POS-3: raw cash handed over. Used to compute real "Change" on the slip —
   * `tendered - grandTotal`. Falls back to amountPaid when not supplied (UPI/card,
   * where tender == booked). Without this the slip always printed "Change ₹0".
   */
  tendered?: number | null
  paymentMode?: string | null
}

export function ReceiptSlip({
  order,
  storeName,
  storeCode,
  customerLabel,
  amountPaid,
  tendered,
  paymentMode,
}: ReceiptProps) {
  const grandTotal = order.grandTotal
  const paid = amountPaid ?? 0
  const tenderedCash = tendered ?? paid
  // Change = cash over the grand total (POS-3). Balance = grand total still owed.
  const change = Math.max(0, tenderedCash - grandTotal)
  const balanceDue = Math.max(0, grandTotal - paid)
  return (
    <div className="print-area mx-auto w-[300px] text-[12px] leading-tight text-black font-mono">
      <div className="text-center space-y-0.5 pb-2">
        <p className="text-base font-bold tracking-wide">{storeName}</p>
        {storeCode && <p className="text-[11px]">Store: {storeCode}</p>}
        <p className="text-[11px]">Counter Receipt</p>
      </div>

      <Divider />

      <Row label="Order" value={order.orderNumber} bold />
      <Row label="Date" value={formatDateTime(order.placedAt)} />
      {customerLabel && <Row label="Customer" value={customerLabel} />}
      <Row label="Type" value={order.isExpress ? 'Express' : 'Standard'} />

      <Divider />

      {order.items && order.items.length > 0 && (
        <div className="space-y-1 py-1">
          {order.items.map((it) => (
            <div key={it.id} className="flex justify-between gap-2">
              <span className="flex-1 truncate">
                {it.itemNameSnapshot}
                <span className="text-[10px]"> · {it.serviceNameSnapshot}</span>
                <br />
                <span className="text-[10px]">
                  {it.quantity} {it.unitOfMeasure} × {formatCurrency(it.unitPrice, order.currencyCode)}
                </span>
              </span>
              <span className="shrink-0">{formatCurrency(it.lineTotal, order.currencyCode)}</span>
            </div>
          ))}
        </div>
      )}

      <Divider />

      <Row label="Subtotal" value={formatCurrency(order.subtotal, order.currencyCode)} />
      {order.addonTotal > 0 && (
        <Row label="Add-ons" value={formatCurrency(order.addonTotal, order.currencyCode)} />
      )}
      {order.expressSurcharge > 0 && (
        <Row label="Express" value={formatCurrency(order.expressSurcharge, order.currencyCode)} />
      )}
      <Row label="Tax (GST)" value={formatCurrency(order.taxTotal, order.currencyCode)} />
      <Row label="TOTAL" value={formatCurrency(order.grandTotal, order.currencyCode)} bold />

      {paid > 0 && (
        <>
          {/* For cash, show what was tendered so the change line reconciles. */}
          {tenderedCash > paid && (
            <Row
              label="Tendered"
              value={formatCurrency(tenderedCash, order.currencyCode)}
            />
          )}
          <Row
            label={`Paid${paymentMode ? ` (${paymentMode})` : ''}`}
            value={formatCurrency(paid, order.currencyCode)}
          />
          {change > 0 && (
            <Row label="Change" value={formatCurrency(change, order.currencyCode)} bold />
          )}
          {balanceDue > 0 && (
            <Row label="Balance Due" value={formatCurrency(balanceDue, order.currencyCode)} bold />
          )}
        </>
      )}
      {/* Pay-later / fully-unpaid order: surface the full balance owed. */}
      {paid === 0 && balanceDue > 0 && (
        <Row label="Balance Due" value={formatCurrency(balanceDue, order.currencyCode)} bold />
      )}

      <Divider />

      <p className="text-center text-[10px] pt-2 leading-snug">
        This is a counter acknowledgement, not a tax invoice.
        <br />
        Tax invoice follows on delivery.
        <br />
        Thank you for choosing {storeName}!
      </p>
    </div>
  )
}

function Row({
  label,
  value,
  bold,
}: {
  label: string
  value: string
  bold?: boolean
}) {
  return (
    <div className={`flex justify-between gap-2 ${bold ? 'font-bold' : ''}`}>
      <span>{label}</span>
      <span>{value}</span>
    </div>
  )
}

function Divider() {
  return <div className="border-t border-dashed border-black my-1" />
}
