/**
 * New Walk-in Order — core POS screen.
 *
 * Flow:
 * 1. Enter customer ID (phone lookup — accepts raw ID or phone; simplified for v1).
 * 2. Select service category → items appear in a touch-friendly grid.
 * 3. Tap an item + service to add a line. Adjust quantity.
 * 4. Review line items in the order cart on the right panel.
 * 5. Submit → POST /api/v1/admin/orders (server resolves prices) → show confirmation.
 */
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Plus, Minus, Trash2, ShoppingCart, Loader2, CheckCircle2 } from 'lucide-react'
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
import { useServiceCategories, useServices, useItems } from '@/hooks/useCatalog'
import { useCreateOrder } from '@/hooks/useOrders'
import { usePosStore } from '@/stores/posStore'
import { formatCurrency } from '@/lib/utils'
import type { OrderDto } from '@/types/api'

interface CartLine {
  itemId: string
  itemName: string
  serviceId: string
  serviceName: string
  quantity: number
}

export function NewOrderPage() {
  const navigate = useNavigate()
  const { activeStore } = usePosStore()

  // ── Form state ────────────────────────────────────────────────────────────
  const [customerId, setCustomerId] = useState('')
  const [isExpress, setIsExpress] = useState(false)
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>('')
  const [selectedServiceId, setSelectedServiceId] = useState<string>('')
  const [cart, setCart] = useState<CartLine[]>([])
  const [serverError, setServerError] = useState<string | null>(null)
  const [confirmedOrder, setConfirmedOrder] = useState<OrderDto | null>(null)

  // ── Data ──────────────────────────────────────────────────────────────────
  const { data: categoriesData, isLoading: loadingCats } = useServiceCategories()
  const { data: servicesData, isLoading: loadingSvcs } = useServices(
    selectedCategoryId ? { categoryId: selectedCategoryId } : {},
  )
  const { data: itemsData, isLoading: loadingItems } = useItems()

  const categories = categoriesData?.list ?? []
  const services = servicesData?.list ?? []
  const items = itemsData?.list ?? []

  const { mutate: createOrder, isPending } = useCreateOrder()

  // ── Cart helpers ──────────────────────────────────────────────────────────

  function addToCart(item: { id: string; name: string }, service: { id: string; name: string }) {
    setCart((prev) => {
      const existing = prev.findIndex(
        (l) => l.itemId === item.id && l.serviceId === service.id,
      )
      if (existing >= 0) {
        return prev.map((l, i) =>
          i === existing ? { ...l, quantity: l.quantity + 1 } : l,
        )
      }
      return [
        ...prev,
        {
          itemId: item.id,
          itemName: item.name,
          serviceId: service.id,
          serviceName: service.name,
          quantity: 1,
        },
      ]
    })
  }

  function updateQty(index: number, delta: number) {
    setCart((prev) =>
      prev
        .map((l, i) => (i === index ? { ...l, quantity: l.quantity + delta } : l))
        .filter((l) => l.quantity > 0),
    )
  }

  function removeLine(index: number) {
    setCart((prev) => prev.filter((_, i) => i !== index))
  }

  // ── Submit ────────────────────────────────────────────────────────────────

  function handleSubmit() {
    if (!customerId.trim()) {
      setServerError('Please enter a customer ID or phone number.')
      return
    }
    if (!activeStore) {
      setServerError('No store selected. Please select a store in the topbar.')
      return
    }
    if (cart.length === 0) {
      setServerError('Add at least one item to the order.')
      return
    }

    setServerError(null)
    createOrder(
      {
        customerId: customerId.trim(),
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
      },
      {
        onSuccess: (order) => {
          setConfirmedOrder(order)
          setCart([])
          setCustomerId('')
        },
        onError: (err) => {
          setServerError(err instanceof Error ? err.message : 'Failed to create order.')
        },
      },
    )
  }

  // ── Confirmation screen ───────────────────────────────────────────────────

  if (confirmedOrder) {
    return (
      <div className="flex flex-col items-center justify-center min-h-full py-16 px-4 gap-6">
        <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center">
          <CheckCircle2 className="h-10 w-10 text-green-600" />
        </div>
        <div className="text-center">
          <h2 className="text-2xl font-bold text-gray-900">Order Placed!</h2>
          <p className="text-gray-500 mt-1">
            Order #{confirmedOrder.orderNumber}
          </p>
        </div>

        <div className="w-full max-w-sm bg-white rounded-2xl border border-gray-200 p-6 space-y-3">
          <div className="flex justify-between text-sm">
            <span className="text-gray-500">Items</span>
            <span className="font-medium">{confirmedOrder.totalItems}</span>
          </div>
          <div className="flex justify-between text-sm">
            <span className="text-gray-500">Subtotal</span>
            <span>{formatCurrency(confirmedOrder.subtotal)}</span>
          </div>
          {confirmedOrder.expressSurcharge > 0 && (
            <div className="flex justify-between text-sm">
              <span className="text-gray-500">Express surcharge</span>
              <span>{formatCurrency(confirmedOrder.expressSurcharge)}</span>
            </div>
          )}
          <div className="flex justify-between text-sm">
            <span className="text-gray-500">Tax</span>
            <span>{formatCurrency(confirmedOrder.taxTotal)}</span>
          </div>
          <div className="border-t border-gray-100 pt-3 flex justify-between font-bold text-lg">
            <span>Total</span>
            <span className="text-blue-700">{formatCurrency(confirmedOrder.grandTotal)}</span>
          </div>
        </div>

        <div className="flex gap-3 w-full max-w-sm">
          <Button
            size="touch"
            variant="outline"
            className="flex-1"
            onClick={() => navigate(`/orders/${confirmedOrder.id}`)}
          >
            View Order
          </Button>
          <Button
            size="touch"
            className="flex-1"
            onClick={() => setConfirmedOrder(null)}
          >
            New Order
          </Button>
        </div>
      </div>
    )
  }

  // ── Main POS layout ───────────────────────────────────────────────────────

  return (
    <div className="flex flex-col lg:flex-row h-full gap-0">
      {/* ── Left Panel: Item Selection ──────────────────────────────────────── */}
      <div className="flex-1 overflow-y-auto p-4 lg:p-6 space-y-4">
        <h1 className="text-xl font-bold text-gray-900">New Walk-in Order</h1>

        {/* Customer + Express */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div className="space-y-2">
            <Label htmlFor="customerId">Customer ID / Phone</Label>
            <Input
              id="customerId"
              type="text"
              placeholder="Enter customer ID or phone…"
              value={customerId}
              onChange={(e) => setCustomerId(e.target.value)}
            />
          </div>
          <div className="space-y-2">
            <Label>Service Type</Label>
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
                Standard
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
                Express
              </button>
            </div>
          </div>
        </div>

        {/* Service Category Tabs */}
        {loadingCats ? (
          <div className="flex items-center gap-2 text-gray-400 py-4">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading categories…</span>
          </div>
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
            <Label>Service</Label>
            {loadingSvcs ? (
              <div className="flex items-center gap-2 text-gray-400">
                <Loader2 className="h-4 w-4 animate-spin" />
                <span className="text-sm">Loading services…</span>
              </div>
            ) : (
              <Select value={selectedServiceId} onValueChange={setSelectedServiceId}>
                <SelectTrigger>
                  <SelectValue placeholder="Pick a service…" />
                </SelectTrigger>
                <SelectContent>
                  {services.map((svc) => (
                    <SelectItem key={svc.id} value={svc.id}>
                      {svc.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>
        )}

        {/* Item Grid */}
        {selectedServiceId && (
          <div className="space-y-2">
            <Label>Items — tap to add</Label>
            {loadingItems ? (
              <div className="flex items-center gap-2 text-gray-400">
                <Loader2 className="h-4 w-4 animate-spin" />
                <span className="text-sm">Loading items…</span>
              </div>
            ) : (
              <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
                {items.filter((i) => i.status === 'active').map((item) => {
                  const inCart = cart.some(
                    (l) => l.itemId === item.id && l.serviceId === selectedServiceId,
                  )
                  const svc = services.find((s) => s.id === selectedServiceId)
                  return (
                    <button
                      key={item.id}
                      type="button"
                      onClick={() => {
                        if (svc) addToCart(
                          { id: item.id, name: item.name },
                          { id: svc.id, name: svc.name },
                        )
                      }}
                      className={`relative flex flex-col items-center justify-center gap-1 p-4 rounded-2xl border-2 min-h-[96px] text-sm font-medium transition-all active:scale-95 ${
                        inCart
                          ? 'border-blue-500 bg-blue-50 text-blue-800'
                          : 'border-gray-200 bg-white text-gray-700 hover:border-blue-300 hover:bg-blue-50'
                      }`}
                    >
                      {inCart && (
                        <span className="absolute top-2 right-2 w-5 h-5 bg-blue-600 text-white text-xs rounded-full flex items-center justify-center">
                          {cart.find((l) => l.itemId === item.id && l.serviceId === selectedServiceId)?.quantity}
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

      {/* ── Right Panel: Cart ─────────────────────────────────────────────────── */}
      <div className="lg:w-80 xl:w-96 flex flex-col border-t lg:border-t-0 lg:border-l border-gray-200 bg-white">
        <div className="p-4 border-b border-gray-100 flex items-center gap-2">
          <ShoppingCart className="h-5 w-5 text-blue-600" />
          <span className="font-semibold text-gray-900">Order Cart</span>
          {cart.length > 0 && (
            <Badge className="ml-auto">{cart.reduce((s, l) => s + l.quantity, 0)} items</Badge>
          )}
        </div>

        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {cart.length === 0 && (
            <p className="text-center text-gray-400 text-sm py-8">
              Tap items on the left to add them here.
            </p>
          )}
          {cart.map((line, idx) => (
            <Card key={idx} className="shadow-none">
              <CardContent className="p-3">
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1 min-w-0">
                    <p className="font-medium text-sm text-gray-900 truncate">{line.itemName}</p>
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
              </CardContent>
            </Card>
          ))}
        </div>

        {/* Submit */}
        <div className="p-4 border-t border-gray-100 space-y-3">
          {serverError && (
            <p className="text-xs text-red-600 text-center">{serverError}</p>
          )}
          {!activeStore && (
            <p className="text-xs text-amber-600 text-center bg-amber-50 rounded-lg p-2">
              No store selected. Use the store switcher in the topbar.
            </p>
          )}
          <Button
            size="touch"
            className="w-full"
            disabled={isPending || cart.length === 0 || !customerId.trim()}
            onClick={handleSubmit}
          >
            {isPending ? (
              <>
                <Loader2 className="h-5 w-5 animate-spin" />
                Creating order…
              </>
            ) : (
              <>
                <ShoppingCart className="h-5 w-5" />
                Place Order ({cart.reduce((s, l) => s + l.quantity, 0)} items)
              </>
            )}
          </Button>
        </div>
      </div>
    </div>
  )
}
