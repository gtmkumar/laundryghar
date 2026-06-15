import { useState } from 'react'
import { Store as StoreIcon, Save } from 'lucide-react'
import { useUpdateStore } from '@/hooks/useTenancy'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useConfirm } from '@/components/shared/useConfirm'
import type { StoreDto, StoreStatus } from '@/types/api'

// Statuses that take the store offline — gated by a confirmation.
const DEACTIVATING_STORE_STATUSES: StoreStatus[] = ['paused', 'closed']

interface Props {
  store: StoreDto | null
  onClose: () => void
}

// Tightly coupled to this drawer's status <select>; one external consumer
// (TenancyPage) reuses the labels. Co-located rather than split into a module.
// eslint-disable-next-line react-refresh/only-export-components
export const STORE_STATUSES: { value: StoreStatus; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'closed', label: 'Closed' },
  { value: 'coming_soon', label: 'Coming soon' },
]

export function StoreEditDrawer({ store, onClose }: Props) {
  const update = useUpdateStore()
  const gate = useConfirm()
  const [name, setName] = useState('')
  const [status, setStatus] = useState<StoreStatus>('active')
  const [contactPhone, setContactPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  // Re-seed the form when a different store is opened. Uses React's documented
  // "adjust state while rendering" pattern (a prev-value tracked in state) rather
  // than an effect, so there's no extra render commit when the drawer opens.
  const [seededId, setSeededId] = useState<string | null>(null)
  if (store && seededId !== store.id) {
    setSeededId(store.id)
    setName(store.name)
    setStatus((store.status as StoreStatus) ?? 'active')
    setContactPhone('')
    setError(null)
  }

  if (!store) return null

  const save = async () => {
    setError(null)
    try {
      await update.mutateAsync({
        id: store.id,
        payload: {
          name: name.trim(),
          status,
          contactPhone: contactPhone.trim() || undefined,
        },
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update the store.')
    }
  }

  const submit = async () => {
    setError(null)
    if (!name.trim()) return setError('Store name is required.')
    // Confirm before taking a previously-active store offline.
    const goingOffline =
      DEACTIVATING_STORE_STATUSES.includes(status) &&
      !DEACTIVATING_STORE_STATUSES.includes((store.status as StoreStatus) ?? 'active')
    if (goingOffline) {
      const label = STORE_STATUSES.find((s) => s.value === status)?.label ?? status
      gate.confirm({
        title: 'Deactivate store?',
        description: `“${store.name}” will be set to ${label} and will stop accepting orders.`,
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
      open={!!store}
      onClose={onClose}
      icon={StoreIcon}
      eyebrow={<>Edit store · <span className="font-mono">{store.code}</span></>}
      title={store.name}
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      submitIcon={Save}
      submitting={update.isPending}
    >
      <Field label="Store name *">
        <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} />
      </Field>
      <Field label="Status">
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value as StoreStatus)}
          className={drawerInputCls}
        >
          {STORE_STATUSES.map((s) => (
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

      <p className="text-xs text-gray-400">
        Code, type, franchise and address are fixed after creation.
      </p>
      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}
