import { useState } from 'react'
import { PackagePlus } from 'lucide-react'
import {
  DrawerSection,
  Field,
  FormDrawer,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import { useCreateGarment } from '@/hooks/useWarehouse'
import type { CreateGarmentRequest } from '@/types/api'

interface Props {
  open: boolean
  onClose: () => void
  warehouseId: string | null
}

/**
 * Add Card drawer — registers a garment into the warehouse flow manually.
 *
 * Scope: assign a physical tag to an order-item and set the initial stage to 'received'.
 * The CreateGarmentCommand already handles: resolving franchise/store from the order,
 * assigning the tag, and setting CurrentStage='received'.
 *
 * Fields the board CAN supply: warehouseId (from board summary).
 * Fields the operator must supply: orderItemId + tagCode.
 * Optional: color, size, weightGrams, attribute flags.
 *
 * The backend does NOT allow creating a garment without a valid order-item —
 * this is by design (garments must trace back to an order for billing/SLA).
 * If the operator doesn't have the order-item ID, they should use the order management
 * screen to locate it.
 */
export function AddCardDrawer({ open, onClose, warehouseId }: Props) {
  const createGarment = useCreateGarment()

  const [form, setForm] = useState<{
    orderItemId: string
    tagCode: string
    color: string
    size: string
    weightGrams: string
    hasOrnaments: boolean
    hasLining: boolean
    isDesignerWear: boolean
  }>({
    orderItemId:    '',
    tagCode:        '',
    color:          '',
    size:           '',
    weightGrams:    '',
    hasOrnaments:   false,
    hasLining:      false,
    isDesignerWear: false,
  })

  const [error, setError] = useState<string | null>(null)

  function resetForm() {
    setForm({
      orderItemId: '', tagCode: '', color: '', size: '', weightGrams: '',
      hasOrnaments: false, hasLining: false, isDesignerWear: false,
    })
    setError(null)
    createGarment.reset()
  }

  function handleClose() {
    resetForm()
    onClose()
  }

  async function handleSubmit() {
    setError(null)
    if (!form.orderItemId.trim()) { setError('Order Item ID is required.'); return }
    if (!form.tagCode.trim())     { setError('Tag code is required.');       return }

    const req: CreateGarmentRequest = {
      orderItemId:    form.orderItemId.trim(),
      tagCode:        form.tagCode.trim().toUpperCase(),
      color:          form.color.trim()  || null,
      size:           form.size.trim()   || null,
      weightGrams:    form.weightGrams   ? parseInt(form.weightGrams, 10) : null,
      hasOrnaments:   form.hasOrnaments,
      hasLining:      form.hasLining,
      isDesignerWear: form.isDesignerWear,
      warehouseId:    warehouseId,
    }

    try {
      await createGarment.mutateAsync(req)
      resetForm()
      onClose()
    } catch (err) {
      setError((err as Error)?.message ?? 'Failed to register garment')
    }
  }

  function set<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  return (
    <FormDrawer
      open={open}
      onClose={handleClose}
      title="Add Card"
      eyebrow="Warehouse"
      icon={PackagePlus}
      width="md"
      error={error}
      onSubmit={handleSubmit}
      submitLabel="Register garment"
      submittingLabel="Registering…"
      submitting={createGarment.isPending}
    >
      <DrawerSection title="Identity">
        <Field label="Order Item ID *" hint="Paste the UUID from the orders screen">
          <input
            type="text"
            className={drawerInputCls}
            value={form.orderItemId}
            onChange={(e) => set('orderItemId', e.target.value)}
            placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            autoComplete="off"
          />
        </Field>
        <Field label="Tag code *" hint="Must be an available (unassigned) tag for this brand">
          <input
            type="text"
            className={drawerInputCls}
            value={form.tagCode}
            onChange={(e) => set('tagCode', e.target.value.toUpperCase())}
            placeholder="LG-XXXX-00000001"
            autoComplete="off"
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Details (optional)">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Color">
            <input
              type="text"
              className={drawerInputCls}
              value={form.color}
              onChange={(e) => set('color', e.target.value)}
              placeholder="e.g. White"
            />
          </Field>
          <Field label="Size">
            <input
              type="text"
              className={drawerInputCls}
              value={form.size}
              onChange={(e) => set('size', e.target.value)}
              placeholder="e.g. L"
            />
          </Field>
        </div>
        <Field label="Weight (grams)">
          <input
            type="number"
            className={drawerInputCls}
            value={form.weightGrams}
            onChange={(e) => set('weightGrams', e.target.value)}
            placeholder="e.g. 450"
            min={0}
          />
        </Field>
      </DrawerSection>

      <DrawerSection title="Attributes">
        {(
          [
            ['hasOrnaments',   'Has ornaments / embellishments'],
            ['hasLining',      'Has lining'],
            ['isDesignerWear', 'Designer / luxury wear'],
          ] as const
        ).map(([key, label]) => (
          <label key={key} className="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
            <input
              type="checkbox"
              className="rounded border-gray-300 text-lg-green"
              checked={form[key]}
              onChange={(e) => set(key, e.target.checked)}
            />
            {label}
          </label>
        ))}
      </DrawerSection>

      {!warehouseId && (
        <p className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700">
          No warehouse context found — the garment will be registered without a warehouse assignment.
          Navigate from the board to have the warehouse pre-filled.
        </p>
      )}
    </FormDrawer>
  )
}
