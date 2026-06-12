/**
 * New Walk-in Order — core POS screen.
 *
 * Flow:
 * 1. Look up / select the customer (phone or name) via the lookup modal.
 * 2. Select a service category → pick a service → tap items to add lines.
 *    Weight-priced services (pricingModel = per_kg) take a kg weight per line;
 *    piece-priced services take an integer quantity.
 * 3. Review the cart. Apply an optional coupon code (server validates on submit).
 * 4. Submit → POST /api/v1/admin/orders (server resolves prices + coupon).
 * 5. Confirmation → collect payment (cash/UPI/card, partial or pay-later),
 *    print counter receipt, print garment tags.
 *
 * POS-1: the in-progress order (lines, customer, express, coupon) lives in a
 *   persisted zustand store scoped to the active store, so a reload doesn't
 *   destroy a half-built basket. A beforeunload guard warns on a non-empty cart.
 * POS-2: order-create carries a client idempotency key (regenerated only when a
 *   submit actually starts) so a double-tap/retry can't duplicate the order.
 */
import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  Plus,
  Minus,
  Trash2,
  ShoppingCart,
  Loader2,
  CheckCircle2,
  User,
  Pencil,
  Ticket,
  Printer,
  Tag,
  Banknote,
  Store,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { ErrorState } from '@/components/shared/ErrorState'
import { EmptyState } from '@/components/shared/EmptyState'
import { useServiceCategories, useServices, useItems } from '@/hooks/useCatalog'
import { useCreateOrder } from '@/hooks/useOrders'
import { useNetworkStatus } from '@/hooks/useNetworkStatus'
import { usePosStore } from '@/stores/posStore'
import { useCartStore, isCartForeign, type CartLine } from '@/stores/cartStore'
import { formatCurrency, customerLabel, newIdempotencyKey } from '@/lib/utils'
import { CustomerLookupModal } from './CustomerLookupModal'
import { PaymentModal, type RecordedPayment } from './PaymentModal'
import { ReceiptSlip } from '@/components/print/Receipt'
import { GarmentTags } from '@/components/print/GarmentTags'
import type { OrderDto, ServiceDto } from '@/types/api'

function isWeightService(svc: ServiceDto | undefined): boolean {
  return svc?.pricingModel === 'per_kg'
}

export function NewOrderPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { activeStore } = usePosStore()
  // R3-POS-5: order submit must reach the server — block it while offline.
  const online = useNetworkStatus()

  // ── Persisted cart (POS-1) ──────────────────────────────────────────────────
  const {
    storeId: cartStoreId,
    customer,
    isExpress,
    coupon,
    lines: cart,
    setStoreId,
    setCustomer,
    setIsExpress,
    setCoupon,
    setLines,
    clearCart,
  } = useCartStore()

  // ── Transient UI state (not worth persisting) ───────────────────────────────
  const [lookupOpen, setLookupOpen] = useState(false)
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>('')
  const [selectedServiceId, setSelectedServiceId] = useState<string>('')
  const [serverError, setServerError] = useState<string | null>(null)

  // ── Confirmation / post-order state ─────────────────────────────────────────
  const [confirmedOrder, setConfirmedOrder] = useState<OrderDto | null>(null)
  const [payment, setPayment] = useState<RecordedPayment | null>(null)
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [printMode, setPrintMode] = useState<'receipt' | 'tags' | null>(null)

  // POS-2: one idempotency key per submit *attempt*; held in a ref so a retry of
  // the same logical submit (e.g. user re-taps after a network blip) reuses it.
  const idempotencyKeyRef = useRef<string | null>(null)

  // POS-1: bind the persisted cart to the active store. If the persisted cart
  // belongs to a different store (device switched context), discard it so we
  // never resurrect another store's basket against the wrong prices.
  useEffect(() => {
    if (!activeStore) return
    if (isCartForeign(cartStoreId, activeStore.id)) {
      clearCart()
    }
    setStoreId(activeStore.id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeStore?.id])

  // POS-1: warn before unload when a non-empty cart would be lost. (The store
  // already persists it, but the native prompt protects against an accidental
  // close mid-edit and is the expected POS behaviour.)
  useEffect(() => {
    if (cart.length === 0 || confirmedOrder) return
    function onBeforeUnload(e: BeforeUnloadEvent) {
      e.preventDefault()
      e.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [cart.length, confirmedOrder])

  // ── Data ──────────────────────────────────────────────────────────────────
  const {
    data: categoriesData,
    isLoading: loadingCats,
    isError: catsError,
    refetch: refetchCats,
  } = useServiceCategories()
  const {
    data: servicesData,
    isLoading: loadingSvcs,
    isError: svcsError,
    refetch: refetchSvcs,
  } = useServices(selectedCategoryId ? { categoryId: selectedCategoryId } : {})
  const {
    data: itemsData,
    isLoading: loadingItems,
    isError: itemsError,
    refetch: refetchItems,
  } = useItems()

  const categories = categoriesData?.list ?? []
  const services = servicesData?.list ?? []
  const items = itemsData?.list ?? []
  const activeItems = items.filter((i) => i.status === 'active')

  const selectedService = services.find((s) => s.id === selectedServiceId)
  const weightMode = isWeightService(selectedService)

  const { mutate: createOrder, isPending } = useCreateOrder()

  // ── Cart helpers ──────────────────────────────────────────────────────────

  function addToCart(item: { id: string; name: string }, service: ServiceDto) {
    const weight = isWeightService(service)
    setLines((prev) => {
      const existing = prev.findIndex(
        (l) => l.itemId === item.id && l.serviceId === service.id,
      )
      if (existing >= 0) {
        if (weight) return prev
        return prev.map((l, i) =>
          i === existing ? { ...l, quantity: l.quantity + 1 } : l,
        )
      }
      const line: CartLine = {
        itemId: item.id,
        itemName: item.name,
        serviceId: service.id,
        serviceName: service.name,
        quantity: weight ? 0.5 : 1,
        isWeight: weight,
      }
      return [...prev, line]
    })
  }

  function updateQty(index: number, delta: number) {
    setLines((prev) =>
      prev
        .map((l, i) => (i === index ? { ...l, quantity: l.quantity + delta } : l))
        .filter((l) => l.quantity > 0),
    )
  }

  function setWeight(index: number, value: number) {
    setLines((prev) => prev.map((l, i) => (i === index ? { ...l, quantity: value } : l)))
  }

  function removeLine(index: number) {
    setLines((prev) => prev.filter((_, i) => i !== index))
  }

  // ── Submit ────────────────────────────────────────────────────────────────

  function handleSubmit() {
    // R3-POS-5: never fire an order while offline — it would fail silently /
    // queue nowhere. The cart is persisted, so it's safe to just block + tell.
    if (!online) {
      setServerError(
        t('pos.offlineSubmit', {
          defaultValue: "You're offline. The cart is saved — submit once the connection is back.",
        }),
      )
      return
    }
    if (!customer) {
      setServerError(t('pos.selectCustomer'))
      return
    }
    if (!activeStore) {
      setServerError(t('pos.noStore'))
      return
    }
    if (cart.length === 0) {
      setServerError(t('pos.addItems'))
      return
    }
    const badWeight = cart.find((l) => l.isWeight && (!l.quantity || l.quantity <= 0))
    if (badWeight) {
      setServerError(
        t('pos.enterWeight', {
          defaultValue: `Enter a weight for ${badWeight.itemName}.`,
          item: badWeight.itemName,
        }),
      )
      return
    }

    setServerError(null)
    // POS-2: mint the key only when a real submit begins; reuse on retry.
    if (!idempotencyKeyRef.current) idempotencyKeyRef.current = newIdempotencyKey()

    createOrder(
      {
        customerId: customer.id,
        storeId: activeStore.id,
        channel: 'walkin',
        isExpress,
        requiresPickup: false,
        requiresDelivery: false,
        pickupAddressId: null,
        deliveryAddressId: null,
        items: cart.map((l) => ({
          itemId: l.itemId,
          serviceId: l.serviceId,
          quantity: l.quantity,
          itemVariantId: null,
          notes: null,
        })),
        addons: [],
        notesCustomer: null,
        couponCode: coupon.trim() || null,
        idempotencyKey: idempotencyKeyRef.current,
      },
      {
        onSuccess: (order) => {
          setConfirmedOrder(order)
          setPayment(null)
          idempotencyKeyRef.current = null
          // POS-1: clear the persisted basket on a successful submit.
          clearCart()
          // Open payment capture immediately.
          setPaymentOpen(true)
        },
        onError: (err) => {
          // Keep the key so a retry of this same order dedupes server-side.
          setServerError(err instanceof Error ? err.message : t('common.error'))
        },
      },
    )
  }

  function startNewOrder() {
    setConfirmedOrder(null)
    setPayment(null)
    clearCart()
    setCustomer(null)
    setSelectedCategoryId('')
    setSelectedServiceId('')
  }

  function handlePrint(mode: 'receipt' | 'tags') {
    setPrintMode(mode)
    setTimeout(() => {
      window.print()
      setPrintMode(null)
    }, 50)
  }

  const cartTotalUnits = cart.reduce((s, l) => s + (l.isWeight ? 1 : l.quantity), 0)

  // ── Confirmation screen ───────────────────────────────────────────────────

  if (confirmedOrder) {
    // POS-4: derive a single payment status for the confirmation summary.
    const balanceDue = payment?.balanceDue ?? (confirmedOrder.amountDue ?? confirmedOrder.grandTotal)
    const isCredit = payment?.credit === true
    const isPaid = payment != null && !isCredit && balanceDue <= 0
    const isPartial = payment != null && !isCredit && (payment.amount > 0) && balanceDue > 0

    return (
      <>
        <div className="flex flex-col items-center justify-center min-h-full py-12 px-4 gap-6 no-print">
          <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center">
            <CheckCircle2 className="h-10 w-10 text-green-600" />
          </div>
          <div className="text-center">
            <h2 className="text-2xl font-bold text-gray-900">{t('pos.orderPlaced')}</h2>
            <p className="text-gray-500 mt-1">Order #{confirmedOrder.orderNumber}</p>
          </div>

          <div className="w-full max-w-sm bg-white rounded-2xl border border-gray-200 p-6 space-y-3">
            <div className="flex justify-between text-sm">
              <span className="text-gray-500">{t('pos.items')}</span>
              <span className="font-medium">{confirmedOrder.totalItems}</span>
            </div>
            <div className="flex justify-between text-sm">
              <span className="text-gray-500">{t('pos.subtotal')}</span>
              <span>{formatCurrency(confirmedOrder.subtotal)}</span>
            </div>
            {confirmedOrder.expressSurcharge > 0 && (
              <div className="flex justify-between text-sm">
                <span className="text-gray-500">{t('pos.expressLabel', { defaultValue: 'Express surcharge' })}</span>
                <span>{formatCurrency(confirmedOrder.expressSurcharge)}</span>
              </div>
            )}
            {confirmedOrder.discountTotal > 0 && (
              <div className="flex justify-between text-sm text-green-700">
                <span>{t('pos.discount')}</span>
                <span>- {formatCurrency(confirmedOrder.discountTotal)}</span>
              </div>
            )}
            <div className="flex justify-between text-sm">
              <span className="text-gray-500">{t('pos.taxLabel', { defaultValue: 'Tax' })}</span>
              <span>{formatCurrency(confirmedOrder.taxTotal)}</span>
            </div>
            <div className="border-t border-gray-100 pt-3 flex justify-between font-bold text-lg">
              <span>{t('pos.total')}</span>
              <span className="text-blue-700">{formatCurrency(confirmedOrder.grandTotal)}</span>
            </div>
            <div className="border-t border-gray-100 pt-3 flex justify-between text-sm">
              <span className="text-gray-500">{t('payment.title', { defaultValue: 'Payment' })}</span>
              {isPaid ? (
                <span className="font-medium text-green-700 capitalize">
                  {formatCurrency(payment!.amount)} · {payment!.method}
                </span>
              ) : isPartial ? (
                <span className="font-medium text-amber-700 capitalize">
                  {formatCurrency(payment!.amount)} · {payment!.method}
                </span>
              ) : isCredit ? (
                <span className="font-medium text-amber-600">
                  {t('pos.onCredit', { defaultValue: 'On credit' })}
                </span>
              ) : (
                <span className="font-medium text-amber-600">{t('pos.unpaid', { defaultValue: 'Unpaid' })}</span>
              )}
            </div>
            {/* POS-4: surface the balance still owed for partial / credit orders. */}
            {(isPartial || isCredit) && balanceDue > 0 && (
              <div className="flex justify-between text-sm font-semibold text-amber-700">
                <span>{t('payment.balanceDue', { defaultValue: 'Balance due' })}</span>
                <span>{formatCurrency(balanceDue)}</span>
              </div>
            )}
          </div>

          {/* Action buttons */}
          <div className="grid grid-cols-2 gap-3 w-full max-w-sm">
            {/* POS-4: a partial/credit order can still "collect balance" here. */}
            {(payment == null || isPartial || isCredit) && balanceDue > 0 && (
              <Button
                size="touch"
                variant="success"
                className="col-span-2"
                onClick={() => setPaymentOpen(true)}
              >
                <Banknote className="h-5 w-5" />{' '}
                {payment == null
                  ? t('pos.collectPayment')
                  : t('pos.collectBalance', { defaultValue: 'Collect balance' })}
              </Button>
            )}
            <Button size="touch" variant="outline" onClick={() => handlePrint('receipt')}>
              <Printer className="h-5 w-5" /> {t('pos.printReceipt')}
            </Button>
            <Button size="touch" variant="outline" onClick={() => handlePrint('tags')}>
              <Tag className="h-5 w-5" /> {t('pos.printTags')}
            </Button>
            <Button
              size="touch"
              variant="secondary"
              onClick={() => navigate(`/orders/${confirmedOrder.id}`)}
            >
              {t('pos.viewOrder', { defaultValue: 'View Order' })}
            </Button>
            <Button size="touch" onClick={startNewOrder}>
              {t('pos.newOrderButton')}
            </Button>
          </div>
        </div>

        {/* Payment modal */}
        <PaymentModal
          open={paymentOpen}
          onClose={() => setPaymentOpen(false)}
          order={confirmedOrder}
          onRecorded={(p) => {
            setPayment(p)
            setPaymentOpen(false)
          }}
        />

        {/* Print payloads (only one mounted at a time). */}
        {printMode === 'receipt' && (
          <ReceiptSlip
            order={confirmedOrder}
            storeName={activeStore?.name ?? 'Laundry Ghar'}
            storeCode={activeStore?.code}
            customerLabel={customer ? customerLabel(customer) : null}
            amountPaid={payment && !payment.credit ? payment.amount : null}
            tendered={payment && !payment.credit ? payment.tendered : null}
            paymentMode={payment && !payment.credit ? payment.method : null}
          />
        )}
        {printMode === 'tags' && (
          <GarmentTags order={confirmedOrder} storeCode={activeStore?.code} />
        )}
      </>
    )
  }

  // ── No store selected (POS-7) ───────────────────────────────────────────────
  if (!activeStore) {
    return (
      <div className="flex flex-col h-full">
        <div className="p-4 lg:p-6">
          <h1 className="text-xl font-bold text-gray-900">{t('pos.newOrder')}</h1>
        </div>
        <EmptyState
          icon={<Store className="h-10 w-10" />}
          title={t('pos.noStoreTitle', { defaultValue: 'No store selected' })}
          hint={t('pos.noStore')}
        />
      </div>
    )
  }

  // ── Main POS layout ───────────────────────────────────────────────────────

  return (
    <>
      <div className="flex flex-col lg:flex-row h-full gap-0">
        {/* ── Left Panel: Item Selection ──────────────────────────────────── */}
        <div className="flex-1 overflow-y-auto p-4 lg:p-6 space-y-4">
          <h1 className="text-xl font-bold text-gray-900">{t('pos.newOrder')}</h1>

          {/* Customer + Express */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="space-y-2">
              <Label>{t('pos.customer')}</Label>
              {customer ? (
                <button
                  type="button"
                  onClick={() => setLookupOpen(true)}
                  className="w-full h-12 px-4 rounded-xl border-2 border-blue-200 bg-blue-50 flex items-center justify-between text-left"
                >
                  <span className="min-w-0">
                    <span className="block font-medium text-blue-900 truncate">
                      {customerLabel(customer)}
                    </span>
                    <span className="block text-xs text-blue-600">{customer.phoneE164}</span>
                  </span>
                  <Pencil className="h-4 w-4 text-blue-500 shrink-0" />
                </button>
              ) : (
                <button
                  type="button"
                  onClick={() => setLookupOpen(true)}
                  className="w-full h-12 px-4 rounded-xl border-2 border-dashed border-gray-300 bg-white flex items-center gap-2 text-gray-500 hover:border-blue-300"
                >
                  <User className="h-4 w-4" />
                  {t('pos.selectCustomer')}…
                </button>
              )}
            </div>
            <div className="space-y-2">
              <Label>{t('pos.service', { defaultValue: 'Service Type' })}</Label>
              <div className="flex gap-3">
                <button
                  type="button"
                  onClick={() => setIsExpress(false)}
                  className={`flex-1 h-12 rounded-xl border-2 text-sm font-medium transition-colors ${
                    !isExpress
                      ? 'border-blue-600 bg-blue-50 text-blue-700'
                      : 'border-gray-200 bg-white text-gray-600'
                  }`}
                >
                  {t('pos.standard', { defaultValue: 'Standard' })}
                </button>
                <button
                  type="button"
                  onClick={() => setIsExpress(true)}
                  className={`flex-1 h-12 rounded-xl border-2 text-sm font-medium transition-colors ${
                    isExpress
                      ? 'border-orange-500 bg-orange-50 text-orange-700'
                      : 'border-gray-200 bg-white text-gray-600'
                  }`}
                >
                  {t('pos.express')}
                </button>
              </div>
            </div>
          </div>

          {/* Service Category Tabs (POS-7: loading / error / empty) */}
          {loadingCats ? (
            <div className="flex items-center gap-2 text-gray-400 py-4">
              <Loader2 className="h-5 w-5 animate-spin" />
              <span className="text-sm">{t('pos.loadingCategories', { defaultValue: 'Loading categories…' })}</span>
            </div>
          ) : catsError ? (
            <ErrorState
              message={t('pos.categoriesError', { defaultValue: 'Could not load categories.' })}
              onRetry={() => refetchCats()}
            />
          ) : categories.length === 0 ? (
            <EmptyState
              compact
              title={t('pos.noCategories', { defaultValue: 'No service categories for this brand.' })}
              hint={t('pos.noCategoriesHint', { defaultValue: 'Add categories in the admin catalog first.' })}
            />
          ) : (
            <div className="flex flex-wrap gap-2">
              {categories.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => {
                    setSelectedCategoryId(cat.id)
                    setSelectedServiceId('')
                  }}
                  className={`px-4 py-2 rounded-full text-sm font-medium border transition-colors ${
                    selectedCategoryId === cat.id
                      ? 'bg-blue-600 text-white border-blue-600'
                      : 'bg-white text-gray-700 border-gray-200 hover:border-blue-300'
                  }`}
                >
                  {cat.name}
                </button>
              ))}
            </div>
          )}

          {/* Service Selector */}
          {selectedCategoryId && (
            <div className="space-y-2">
              <Label>{t('pos.service')}</Label>
              {loadingSvcs ? (
                <div className="flex items-center gap-2 text-gray-400">
                  <Loader2 className="h-4 w-4 animate-spin" />
                  <span className="text-sm">{t('pos.loadingServices', { defaultValue: 'Loading services…' })}</span>
                </div>
              ) : svcsError ? (
                <ErrorState
                  message={t('pos.servicesError', { defaultValue: 'Could not load services.' })}
                  onRetry={() => refetchSvcs()}
                />
              ) : services.length === 0 ? (
                <EmptyState
                  compact
                  title={t('pos.noServices', { defaultValue: 'No services in this category.' })}
                />
              ) : (
                <Select value={selectedServiceId} onValueChange={setSelectedServiceId}>
                  <SelectTrigger>
                    <SelectValue placeholder={`${t('pos.selectService')}…`} />
                  </SelectTrigger>
                  <SelectContent>
                    {services.map((svc) => (
                      <SelectItem key={svc.id} value={svc.id}>
                        {svc.name}
                        {svc.pricingModel === 'per_kg' ? ' (per kg)' : ''}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
          )}

          {/* Item Grid */}
          {selectedServiceId && selectedService && (
            <div className="space-y-2">
              <Label>
                {t('pos.items')}{weightMode ? ` (${t('pos.weight')})` : ''}
              </Label>
              {loadingItems ? (
                <div className="flex items-center gap-2 text-gray-400">
                  <Loader2 className="h-4 w-4 animate-spin" />
                  <span className="text-sm">{t('pos.loadingItems', { defaultValue: 'Loading items…' })}</span>
                </div>
              ) : itemsError ? (
                <ErrorState
                  message={t('pos.itemsError', { defaultValue: 'Could not load items.' })}
                  onRetry={() => refetchItems()}
                />
              ) : activeItems.length === 0 ? (
                <EmptyState
                  compact
                  title={t('pos.noItems', { defaultValue: 'No active items for this brand.' })}
                />
              ) : (
                <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
                  {activeItems.map((item) => {
                    const inCart = cart.some(
                      (l) => l.itemId === item.id && l.serviceId === selectedServiceId,
                    )
                    return (
                      <button
                        key={item.id}
                        type="button"
                        onClick={() =>
                          addToCart({ id: item.id, name: item.name }, selectedService)
                        }
                        className={`relative flex flex-col items-center justify-center gap-1 p-4 rounded-2xl border-2 min-h-[96px] text-sm font-medium transition-all active:scale-95 ${
                          inCart
                            ? 'border-blue-500 bg-blue-50 text-blue-800'
                            : 'border-gray-200 bg-white text-gray-700 hover:border-blue-300 hover:bg-blue-50'
                        }`}
                      >
                        {inCart && !weightMode && (
                          <span className="absolute top-2 right-2 w-5 h-5 bg-blue-600 text-white text-xs rounded-full flex items-center justify-center">
                            {
                              cart.find(
                                (l) =>
                                  l.itemId === item.id &&
                                  l.serviceId === selectedServiceId,
                              )?.quantity
                            }
                          </span>
                        )}
                        <span className="text-2xl">
                          {item.iconUrl ? (
                            <img src={item.iconUrl} alt="" className="w-8 h-8 object-cover" />
                          ) : (
                            '🧺'
                          )}
                        </span>
                        <span className="text-center leading-tight">{item.name}</span>
                        {inCart && <Plus className="h-4 w-4 text-blue-500" />}
                      </button>
                    )
                  })}
                </div>
              )}
            </div>
          )}
        </div>

        {/* ── Right Panel: Cart ──────────────────────────────────────────────── */}
        <div className="lg:w-80 xl:w-96 flex flex-col border-t lg:border-t-0 lg:border-l border-gray-200 bg-white">
          <div className="p-4 border-b border-gray-100 flex items-center gap-2">
            <ShoppingCart className="h-5 w-5 text-blue-600" />
            <span className="font-semibold text-gray-900">{t('pos.cart')}</span>
            {cart.length > 0 && (
              <Badge className="ml-auto">{cartTotalUnits} {t('pos.items')}</Badge>
            )}
          </div>

          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            {cart.length === 0 && (
              <p className="text-center text-gray-400 text-sm py-8">
                {t('pos.addItems')}
              </p>
            )}
            {cart.map((line, idx) => (
              <Card key={`${line.itemId}-${line.serviceId}`} className="shadow-none">
                <CardContent className="p-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex-1 min-w-0">
                      <p className="font-medium text-sm text-gray-900 truncate">
                        {line.itemName}
                      </p>
                      <p className="text-xs text-gray-500 truncate">{line.serviceName}</p>
                    </div>
                    <button
                      type="button"
                      onClick={() => removeLine(idx)}
                      className="text-gray-300 hover:text-red-500 p-1"
                      aria-label="Remove"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>

                  {line.isWeight ? (
                    <div className="flex items-center gap-2 mt-2">
                      <Input
                        type="number"
                        min="0.1"
                        step="0.1"
                        inputMode="decimal"
                        value={line.quantity}
                        aria-invalid={!line.quantity || line.quantity <= 0}
                        onChange={(e) => setWeight(idx, parseFloat(e.target.value) || 0)}
                        className="h-9 w-24"
                        aria-label={`Weight in kg for ${line.itemName}`}
                      />
                      <span className="text-sm text-gray-500">{t('pos.kgUnit', { defaultValue: 'kg' })}</span>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2 mt-2">
                      <button
                        type="button"
                        onClick={() => updateQty(idx, -1)}
                        className="w-8 h-8 flex items-center justify-center rounded-lg border border-gray-200 hover:bg-gray-50 text-gray-600"
                        aria-label="Decrease"
                      >
                        <Minus className="h-4 w-4" />
                      </button>
                      <span className="w-8 text-center font-semibold text-gray-900">
                        {line.quantity}
                      </span>
                      <button
                        type="button"
                        onClick={() => updateQty(idx, 1)}
                        className="w-8 h-8 flex items-center justify-center rounded-lg border border-gray-200 hover:bg-gray-50 text-gray-600"
                        aria-label="Increase"
                      >
                        <Plus className="h-4 w-4" />
                      </button>
                    </div>
                  )}
                </CardContent>
              </Card>
            ))}
          </div>

          {/* Coupon + Submit */}
          <div className="p-4 border-t border-gray-100 space-y-3">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <div className="relative flex-1">
                  <Ticket className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
                  <Input
                    placeholder={t('pos.couponPlaceholder')}
                    className="pl-9 uppercase"
                    value={coupon}
                    onChange={(e) => setCoupon(e.target.value.toUpperCase())}
                  />
                </div>
                <Button
                  variant="outline"
                  size="default"
                  type="button"
                  onClick={() => setCoupon('')}
                  disabled={!coupon}
                >
                  Clear
                </Button>
              </div>
              {coupon && (
                <p className="text-[11px] text-blue-600">
                  Coupon "{coupon}" will be validated on submit.
                </p>
              )}
            </div>

            {serverError && <p className="text-xs text-red-600 text-center">{serverError}</p>}
            {!online && (
              <p className="text-xs text-amber-600 text-center">
                {t('pos.offlineSubmit', {
                  defaultValue: "You're offline. The cart is saved — submit once the connection is back.",
                })}
              </p>
            )}
            <Button
              size="touch"
              className="w-full"
              disabled={isPending || cart.length === 0 || !customer || !online}
              onClick={handleSubmit}
            >
              {isPending ? (
                <>
                  <Loader2 className="h-5 w-5 animate-spin" />
                  {t('pos.placingOrder')}
                </>
              ) : (
                <>
                  <ShoppingCart className="h-5 w-5" />
                  {t('pos.placeOrder')} ({cartTotalUnits} {t('pos.items')})
                </>
              )}
            </Button>
          </div>
        </div>
      </div>

      <CustomerLookupModal
        open={lookupOpen}
        onClose={() => setLookupOpen(false)}
        onSelect={(c) => setCustomer(c)}
      />
    </>
  )
}
