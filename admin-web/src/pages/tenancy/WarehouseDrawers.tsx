import { useEffect, useMemo, useState } from 'react'
import { Loader2, Warehouse as WarehouseIcon, Plus, Save, Pencil, Lock } from 'lucide-react'
import { useFranchises, useCreateWarehouse, useUpdateWarehouse } from '@/hooks/useTenancy'
import { useOnboardingState } from '@/hooks/useOnboarding'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import { FormDrawer, DrawerSection, Field, drawerInputCls, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
import { ConfirmDialog, useConfirm } from '@/components/shared/ConfirmDialog'
import { Badge } from '@/components/ui/badge'
import type { WarehouseDto, WarehouseType, WarehouseStatus } from '@/types/api'
import { formatDate } from '@/lib/utils'

// Statuses that take the warehouse offline — gated by a confirmation.
const DEACTIVATING_WAREHOUSE_STATUSES: WarehouseStatus[] = ['paused', 'maintenance', 'closed']

const WAREHOUSE_TYPES: { value: WarehouseType; label: string }[] = [
  { value: 'central', label: 'Central' },
  { value: 'satellite', label: 'Satellite' },
  { value: 'express', label: 'Express' },
  { value: 'specialty', label: 'Specialty' },
]

export const WAREHOUSE_STATUSES: { value: WarehouseStatus; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'maintenance', label: 'Maintenance' },
  { value: 'closed', label: 'Closed' },
]

// ── Add ───────────────────────────────────────────────────────────────────────

const blankForm = {
  franchiseId: '',
  code: '',
  name: '',
  warehouseType: 'central' as WarehouseType,
  addressLine1: '',
  city: '',
  state: '',
  pincode: '',
  sameAsFranchise: false,
}

export function AddWarehouseDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const brandId = useEffectiveBrandId()
  const { isFranchiseScoped, franchiseId: scopedFranchiseId } = usePermissions()
  const franchisesQ = useFranchises({ brandId: brandId ?? undefined, pageSize: 100 })
  const createWarehouse = useCreateWarehouse()

  const [form, setForm] = useState(blankForm)
  const [error, setError] = useState<string | null>(null)

  const lockFranchise = isFranchiseScoped && !!scopedFranchiseId

  useEffect(() => {
    if (open) {
      setForm({ ...blankForm, franchiseId: lockFranchise ? scopedFranchiseId! : '' })
      setError(null)
    }
  }, [open, lockFranchise, scopedFranchiseId])

  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const lockedFranchiseName = useMemo(
    () => (lockFranchise ? franchises.find((f) => f.id === scopedFranchiseId)?.legalName : undefined),
    [lockFranchise, franchises, scopedFranchiseId],
  )

  const onboardingQ = useOnboardingState(form.franchiseId || null)
  const franchiseAddress =
    onboardingQ.data?.operationalAddress ?? onboardingQ.data?.billingAddress ?? null
  const hasFranchiseAddress = !!(
    franchiseAddress &&
    (franchiseAddress.line1 || franchiseAddress.city || franchiseAddress.pincode)
  )

  useEffect(() => {
    if (!form.sameAsFranchise || !franchiseAddress) return
    setForm((f) => ({
      ...f,
      addressLine1: franchiseAddress.line1 ?? '',
      city: franchiseAddress.city ?? '',
      state: franchiseAddress.state ?? '',
      pincode: franchiseAddress.pincode ?? '',
    }))
  }, [form.sameAsFranchise, franchiseAddress])

  if (!open) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const addrLocked = form.sameAsFranchise && hasFranchiseAddress
  const onFranchiseChange = (value: string) =>
    setForm((f) => ({ ...f, franchiseId: value, sameAsFranchise: false }))
  const toggleSameAsFranchise = (checked: boolean) =>
    setForm((f) =>
      checked
        ? { ...f, sameAsFranchise: true }
        : { ...f, sameAsFranchise: false, addressLine1: '', city: '', state: '', pincode: '' },
    )

  const submit = async () => {
    setError(null)
    if (!brandId) return setError('No active brand selected.')
    if (!form.franchiseId) return setError('Pick a franchise for this warehouse.')
    if (!form.code.trim()) return setError('Warehouse code is required.')
    if (!form.name.trim()) return setError('Warehouse name is required.')
    if (!form.addressLine1.trim()) return setError('Address is required.')
    if (!form.city.trim()) return setError('City is required.')
    if (!form.state.trim()) return setError('State is required.')
    if (!form.pincode.trim()) return setError('Pincode is required.')

    try {
      await createWarehouse.mutateAsync({
        brandId,
        franchiseId: form.franchiseId,
        code: form.code.trim(),
        name: form.name.trim(),
        addressLine1: form.addressLine1.trim(),
        city: form.city.trim(),
        state: form.state.trim(),
        pincode: form.pincode.trim(),
        warehouseType: form.warehouseType,
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the warehouse.')
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      eyebrow="Tenancy"
      title="Add warehouse"
      icon={WarehouseIcon}
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel="Add warehouse"
      submittingLabel="Creating…"
      submitIcon={Plus}
      submitting={createWarehouse.isPending}
    >
      <DrawerSection title="Franchise & identity">
        <Field label="Franchise *">
          {lockFranchise ? (
            <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700">
              <Lock className="h-3.5 w-3.5 shrink-0 text-gray-400" />
              <span className="truncate">{lockedFranchiseName ?? 'Your franchise'}</span>
            </div>
          ) : (
            <select
              value={form.franchiseId}
              onChange={(e) => onFranchiseChange(e.target.value)}
              className={drawerInputCls}
              disabled={franchisesQ.isLoading}
            >
              <option value="">
                {franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}
              </option>
              {franchises.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.legalName} ({f.code})
                </option>
              ))}
            </select>
          )}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Warehouse code *">
            <input
              value={form.code}
              onChange={(e) => set('code', e.target.value.toUpperCase())}
              className={drawerInputCls}
              placeholder="WH-GGN-002"
            />
          </Field>
          <Field label="Warehouse type">
            <select
              value={form.warehouseType}
              onChange={(e) => set('warehouseType', e.target.value as WarehouseType)}
              className={drawerInputCls}
            >
              {WAREHOUSE_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <Field label="Warehouse name *">
          <input
            value={form.name}
            onChange={(e) => set('name', e.target.value)}
            className={drawerInputCls}
            placeholder="Laundry Ghar Gurgaon Hub"
          />
        </Field>
      </DrawerSection>

      <DrawerSection
        title={
          <span className="flex items-center justify-between">
            <span>Location</span>
            {onboardingQ.isFetching && form.franchiseId && (
              <Loader2 className="h-3.5 w-3.5 animate-spin text-gray-300" />
            )}
          </span>
        }
      >
        <label
          className={[
            'flex items-center gap-2 rounded-lg border px-3 py-2 text-sm',
            form.franchiseId && hasFranchiseAddress
              ? 'cursor-pointer border-gray-200 text-gray-700 hover:bg-gray-50'
              : 'cursor-not-allowed border-gray-100 text-gray-400',
          ].join(' ')}
        >
          <input
            type="checkbox"
            checked={addrLocked}
            disabled={!form.franchiseId || !hasFranchiseAddress}
            onChange={(e) => toggleSameAsFranchise(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          <span>
            Same as franchise address
            {form.franchiseId && !hasFranchiseAddress && !onboardingQ.isFetching && (
              <span className="text-gray-400"> — franchise has no saved address</span>
            )}
            {!form.franchiseId && <span className="text-gray-400"> — pick a franchise first</span>}
          </span>
        </label>

        <Field label="Address *">
          <input
            value={form.addressLine1}
            onChange={(e) => set('addressLine1', e.target.value)}
            className={drawerInputCls}
            placeholder="Plot 5, Industrial Area"
            disabled={addrLocked}
          />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="City *">
            <input value={form.city} onChange={(e) => set('city', e.target.value)} className={drawerInputCls} placeholder="Gurgaon" disabled={addrLocked} />
          </Field>
          <Field label="State *">
            <input value={form.state} onChange={(e) => set('state', e.target.value)} className={drawerInputCls} placeholder="Haryana" disabled={addrLocked} />
          </Field>
        </div>
        <Field label="Pincode *">
          <input value={form.pincode} onChange={(e) => set('pincode', e.target.value)} className={drawerInputCls} placeholder="122001" inputMode="numeric" disabled={addrLocked} />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── View ────────────────────────────────────────────────────────────────────

export function WarehouseDetailDrawer({
  warehouse,
  franchiseName,
  onClose,
  onEdit,
  canManage,
}: {
  warehouse: WarehouseDto | null
  franchiseName?: string
  onClose: () => void
  onEdit?: (w: WarehouseDto) => void
  canManage?: boolean
}) {
  return (
    <FormDrawer
      open={!!warehouse}
      onClose={onClose}
      icon={WarehouseIcon}
      eyebrow="Warehouse"
      title={warehouse?.name ?? 'Warehouse'}
      width="sm"
      footer={
        warehouse && canManage && onEdit ? (
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => onEdit(warehouse)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              <Pencil className="h-3.5 w-3.5" /> Edit
            </button>
          </div>
        ) : undefined
      }
    >
      {warehouse && (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-3">
            <Badge variant={warehouse.status === 'active' ? 'success' : warehouse.status === 'closed' ? 'secondary' : 'warning'} className="capitalize">
              {warehouse.status.replace(/_/g, ' ')}
            </Badge>
            <span className="rounded-full bg-gray-100 px-2.5 py-1 font-mono text-xs text-gray-600">
              {warehouse.code}
            </span>
          </div>
          <DetailSection title="Details">
            <DetailRow label="Name" value={warehouse.name} />
            <DetailRow label="Franchise" value={franchiseName ?? '—'} />
            <DetailRow label="City" value={warehouse.city} />
            <DetailRow label="Created" value={formatDate(warehouse.createdAt)} />
          </DetailSection>
        </div>
      )}
    </FormDrawer>
  )
}

// ── Edit ────────────────────────────────────────────────────────────────────

export function WarehouseEditDrawer({
  warehouse,
  onClose,
}: {
  warehouse: WarehouseDto | null
  onClose: () => void
}) {
  const update = useUpdateWarehouse()
  const gate = useConfirm()
  const [name, setName] = useState('')
  const [status, setStatus] = useState<WarehouseStatus>('active')
  const [contactPhone, setContactPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (warehouse) {
      setName(warehouse.name)
      setStatus((warehouse.status as WarehouseStatus) ?? 'active')
      setContactPhone('')
      setError(null)
    }
  }, [warehouse])

  if (!warehouse) return null

  const save = async () => {
    setError(null)
    try {
      await update.mutateAsync({
        id: warehouse.id,
        payload: { name: name.trim(), status, contactPhone: contactPhone.trim() || undefined },
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update the warehouse.')
    }
  }

  const submit = async () => {
    setError(null)
    if (!name.trim()) return setError('Warehouse name is required.')
    // Confirm before taking a previously-active warehouse offline.
    const goingOffline =
      DEACTIVATING_WAREHOUSE_STATUSES.includes(status) &&
      !DEACTIVATING_WAREHOUSE_STATUSES.includes((warehouse.status as WarehouseStatus) ?? 'active')
    if (goingOffline) {
      const label = WAREHOUSE_STATUSES.find((s) => s.value === status)?.label ?? status
      gate.confirm({
        title: 'Deactivate warehouse?',
        description: `“${warehouse.name}” will be set to ${label} and will stop processing operations.`,
        confirmLabel: `Set ${label}`,
        tone: 'danger',
        onConfirm: () => save(),
      })
      return
    }
    await save()
  }

  return (
    <FormDrawer
      open={!!warehouse}
      onClose={onClose}
      icon={WarehouseIcon}
      eyebrow={<>Edit warehouse · <span className="font-mono">{warehouse.code}</span></>}
      title={warehouse.name}
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      submitIcon={Save}
      submitting={update.isPending}
    >
      <Field label="Warehouse name *">
        <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} />
      </Field>
      <Field label="Status">
        <select value={status} onChange={(e) => setStatus(e.target.value as WarehouseStatus)} className={drawerInputCls}>
          {WAREHOUSE_STATUSES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </Field>
      <Field label="Contact phone">
        <input
          value={contactPhone}
          onChange={(e) => setContactPhone(e.target.value)}
          className={drawerInputCls}
          placeholder="Leave blank to keep unchanged"
          inputMode="tel"
        />
      </Field>
      <p className="text-xs text-gray-400">Code, type, franchise and address are fixed after creation.</p>
      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}

