/**
 * OrderCard — the "Active orders" grid card on the Orders page.
 *
 * Shows order #, customer, store, items count, total, status badge, age since
 * placed, express/payment chips and a promised-by countdown when present.
 * Newly-arrived cards (during a live poll) get a green ring pulse via the
 * `isNew` flag → the `.lg-new-pulse` keyframe.
 */
import { useTranslation } from 'react-i18next'
import { Zap, Clock, Package } from 'lucide-react'
import type { OrderDto } from '@/types/api'
import { formatCurrency, cn } from '@/lib/utils'
import {
  useStatusLabel,
  formatDurationMinutes,
  minutesSince,
  minutesUntil,
  paymentTone,
  PAYMENT_TONE_CLASS,
} from './orderFormat'
import { statusBadgeVariant } from './orderStatus'
import { Badge } from '@/components/ui/badge'

interface Props {
  order: OrderDto
  customerName: string
  storeName: string
  isNew: boolean
  onClick: () => void
}

export function OrderCard({ order, customerName, storeName, isNew, onClick }: Props) {
  const { t } = useTranslation()
  const labelFor = useStatusLabel()

  const ageMin = minutesSince(order.placedAt)
  const promisedMin = order.promisedDeliveryAt ? minutesUntil(order.promisedDeliveryAt) : null

  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex flex-col gap-3 rounded-2xl border border-gray-200 bg-white p-4 text-left shadow-[0_1px_2px_rgba(16,16,16,0.04)] transition-all hover:border-lg-green/40 hover:shadow-md',
        isNew && 'lg-new-pulse border-lg-green/50',
      )}
    >
      {/* Header: number + status */}
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate font-mono text-sm font-semibold text-gray-900">{order.orderNumber}</p>
          <p className="mt-0.5 truncate text-xs text-gray-500">
            {customerName} · {storeName}
          </p>
        </div>
        <Badge variant={statusBadgeVariant(order.status)} className="shrink-0">
          {labelFor(order.status)}
        </Badge>
      </div>

      {/* Chips */}
      <div className="flex flex-wrap items-center gap-1.5">
        {order.isExpress && (
          <span className="inline-flex items-center gap-0.5 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-700">
            <Zap className="h-3 w-3" /> {t('orders.express')}
          </span>
        )}
        <span
          className={cn(
            'rounded-full border px-2 py-0.5 text-[11px] font-medium capitalize',
            PAYMENT_TONE_CLASS[paymentTone(order.paymentStatus)],
          )}
        >
          {t(`orders.payment.${order.paymentStatus}`, { defaultValue: order.paymentStatus })}
        </span>
        {order.channel && (
          <span className="rounded-full bg-gray-100 px-2 py-0.5 text-[11px] font-medium capitalize text-gray-500">
            {order.channel}
          </span>
        )}
      </div>

      {/* Footer: items + total + age/countdown */}
      <div className="flex items-end justify-between border-t border-gray-100 pt-2.5">
        <div className="flex items-center gap-3 text-xs text-gray-500">
          <span className="inline-flex items-center gap-1">
            <Package className="h-3.5 w-3.5" />
            {t('orders.items', { count: order.totalItems })}
          </span>
          <span className="inline-flex items-center gap-1 text-gray-400">
            <Clock className="h-3.5 w-3.5" />
            {t('orders.placedAgo', { age: formatDurationMinutes(ageMin) })}
          </span>
        </div>
        <span className="text-sm font-bold tabular text-gray-900">
          {formatCurrency(order.grandTotal, order.currencyCode)}
        </span>
      </div>

      {/* Promised-by countdown */}
      {promisedMin !== null && (
        <div
          className={cn(
            'flex items-center gap-1 rounded-lg px-2 py-1 text-[11px] font-medium',
            promisedMin < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-700',
          )}
        >
          <Clock className="h-3 w-3" />
          {promisedMin < 0
            ? t('orders.overdueBy', { time: formatDurationMinutes(Math.abs(promisedMin)) })
            : t('orders.dueIn', { time: formatDurationMinutes(promisedMin) })}
        </div>
      )}
    </button>
  )
}
