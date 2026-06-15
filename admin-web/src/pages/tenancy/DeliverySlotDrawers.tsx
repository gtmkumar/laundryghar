import { useMemo, useState } from 'react'
import { CalendarClock, Plus, Save } from 'lucide-react'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { useCreateDeliverySlot, useUpdateDeliverySlot } from '@/hooks/usePickups'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import type { DeliverySlotDto, DeliverySlotType, StoreDto } from '@/types/api'

// Small constant co-located with this drawer's <select>; not worth a separate module.
// eslint-disable-next-line react-refresh/only-export-components
export const SLOT_TYPES: { value: DeliverySlotType; label: string }[] = [
  { value: 'pickup', label: 'Pickup' },
  { value: 'delivery', label: 'Delivery' },
]

// Small constant co-located with this drawer's <select>; not worth a separate module.
// eslint-disable-next-line react-refresh/only-export-components
export const SLOT_STATUSES: { value: string; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'closed', label: 'Closed' },
]

/** "HH:mm" (from <input type=time>) → "HH:mm:ss" for the TimeOnly backend field. */
function toTimeOnly(v: string): string {
  return v.length === 5 ? `${v}:00` : v
}

/** "HH:mm:ss" → "HH:mm" for binding back into <input type=time>. */
function toInputTime(v: string): string {
  return v.length >= 5 ? v.slice(0, 5) : v
}

// ── Add slot ────────────────────────────────────────────────────────────────────

const blankForm = {
  storeId: '',
  slotDate: '',
  slotStart: '09:00',
  slotEnd: '11:00',
  slotType: 'pickup' as DeliverySlotType,
  capacity: 10,
  isExpress: false,
}

export function AddSlotDrawer({
  open,
  onClose,
  stores,
}: {
  open: boolean
  onClose: () => void
  stores: StoreDto[]
}) {
  const create = useCreateDeliverySlot()
  const [form, setForm] = useState(blankForm)
  const [error, setError] = useState<string | null>(null)

  // Re-seed the form on each open (adjust-state-while-rendering, not an effect).
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setForm(blankForm)
      setError(null)
    }
  }

  if (!open) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const submit = async () => {
    setError(null)
    if (!form.storeId) return setError('Pick a store for this slot.')
    if (!form.slotDate) return setError('Pick a date.')
    if (!form.slotStart || !form.slotEnd) return setError('Set the start and end times.')
    if (toTimeOnly(form.slotEnd) <= toTimeOnly(form.slotStart))
      return setError('The end time must be after the start time.')
    if (!Number.isFinite(form.capacity) || form.capacity < 1)
      return setError('Capacity must be at least 1.')

    try {
      await create.mutateAsync({
        storeId: form.storeId,
        slotDate: form.slotDate,
        slotStart: toTimeOnly(form.slotStart),
        slotEnd: toTimeOnly(form.slotEnd),
        slotType: form.slotType,
        capacity: form.capacity,
        isExpress: form.isExpress,
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the slot.')
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      eyebrow="Tenancy"
      title="Add delivery slot"
      icon={CalendarClock}
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel="Add slot"
      submittingLabel="Creating…"
      submitIcon={Plus}
      submitting={create.isPending}
    >
      <DrawerSection title="Slot">
        <Field label="Store *">
          <select
            value={form.storeId}
            onChange={(e) => set('storeId', e.target.value)}
            className={drawerInputCls}
          >
            <option value="">Select a store…</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.code})
              </option>
            ))}
          </select>
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label="Type">
            <select
              value={form.slotType}
              onChange={(e) => set('slotType', e.target.value as DeliverySlotType)}
              className={drawerInputCls}
            >
              {SLOT_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Date *">
            <input
              type="date"
              value={form.slotDate}
              onChange={(e) => set('slotDate', e.target.value)}
              className={drawerInputCls}
            />
          </Field>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <Field label="Start *">
            <input
              type="time"
              value={form.slotStart}
              onChange={(e) => set('slotStart', e.target.value)}
              className={drawerInputCls}
            />
          </Field>
          <Field label="End *">
            <input
              type="time"
              value={form.slotEnd}
              onChange={(e) => set('slotEnd', e.target.value)}
              className={drawerInputCls}
            />
          </Field>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <Field label="Capacity *">
            <input
              type="number"
              min={1}
              value={form.capacity}
              onChange={(e) => set('capacity', Number(e.target.value))}
              className={drawerInputCls}
              inputMode="numeric"
            />
          </Field>
          <Field label="Express">
            <label className="flex h-[38px] items-center gap-2 rounded-lg border border-gray-200 px-3 text-sm text-gray-700">
              <input
                type="checkbox"
                checked={form.isExpress}
                onChange={(e) => set('isExpress', e.target.checked)}
                className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
              />
              Express slot
            </label>
          </Field>
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Edit slot (capacity + active/status only, per UpdateDeliverySlotRequest) ──────

export function EditSlotDrawer({
  slot,
  onClose,
  storeName,
}: {
  slot: DeliverySlotDto | null
  onClose: () => void
  storeName?: string
}) {
  const update = useUpdateDeliverySlot()
  const [capacity, setCapacity] = useState(0)
  const [isActive, setIsActive] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Seed the form from the slot (adjust-state-while-rendering, keyed on id).
  const [seededId, setSeededId] = useState<string | null>(null)
  if (slot && seededId !== slot.id) {
    setSeededId(slot.id)
    setCapacity(slot.capacity)
    setIsActive(slot.isActive)
    setError(null)
  }

  if (!slot) return null

  const submit = async () => {
    setError(null)
    if (!Number.isFinite(capacity) || capacity < 1) return setError('Capacity must be at least 1.')
    if (capacity < slot.bookedCount)
      return setError(`Capacity can't be below the ${slot.bookedCount} already booked.`)

    try {
      await update.mutateAsync({
        id: slot.id,
        payload: { capacity, isActive, status: isActive ? 'active' : 'closed' },
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update the slot.')
    }
  }

  return (
    <FormDrawer
      open={!!slot}
      onClose={onClose}
      icon={CalendarClock}
      eyebrow={<>Edit slot{storeName ? <> · {storeName}</> : null}</>}
      title={`${slot.slotDate} · ${toInputTime(slot.slotStart)}–${toInputTime(slot.slotEnd)}`}
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      submitIcon={Save}
      submitting={update.isPending}
    >
      <Field label="Capacity *" hint={`${slot.bookedCount} booked so far.`}>
        <input
          type="number"
          min={Math.max(1, slot.bookedCount)}
          value={capacity}
          onChange={(e) => setCapacity(Number(e.target.value))}
          className={drawerInputCls}
          inputMode="numeric"
        />
      </Field>

      <Field label="Availability">
        <label className="flex items-center gap-2 rounded-lg border border-gray-200 px-3 py-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Active — customers can book this slot
        </label>
      </Field>

      <p className="text-xs text-gray-400">
        Date, time window, type and store are fixed after creation.
      </p>
    </FormDrawer>
  )
}

// ── Store-picker hook used by the slots tab + add drawer ──────────────────────────

/** All stores for the active brand (one page is enough for the picker/lookup). */
// Store-picker hook co-located with the slots drawers; not worth a separate module.
// eslint-disable-next-line react-refresh/only-export-components
export function useSlotStores() {
  const brandId = useEffectiveBrandId()
  const storesQ = useStores({ brandId: brandId ?? undefined, pageSize: 100 })
  const stores = useMemo(() => storesQ.data?.list ?? [], [storesQ.data])
  const storeName = useMemo(() => {
    const m = new Map<string, string>()
    for (const s of stores) m.set(s.id, s.name)
    return m
  }, [stores])
  return { stores, storeName, isLoading: storesQ.isLoading }
}
