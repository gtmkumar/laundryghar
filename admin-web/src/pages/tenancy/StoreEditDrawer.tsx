import { useEffect, useState } from 'react'
import { Store as StoreIcon, Save } from 'lucide-react'
import { useUpdateStore } from '@/hooks/useTenancy'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import type { StoreDto, StoreStatus } from '@/types/api'

interface Props {
  store: StoreDto | null
  onClose: () => void
}

export const STORE_STATUSES: { value: StoreStatus; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'closed', label: 'Closed' },
  { value: 'coming_soon', label: 'Coming soon' },
]

export function StoreEditDrawer({ store, onClose }: Props) {
  const update = useUpdateStore()
  const [name, setName] = useState('')
  const [status, setStatus] = useState<StoreStatus>('active')
  const [contactPhone, setContactPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (store) {
      setName(store.name)
      setStatus((store.status as StoreStatus) ?? 'active')
      setContactPhone('')
      setError(null)
    }
  }, [store])

  if (!store) return null

  const submit = async () => {
    setError(null)
    if (!name.trim()) return setError('Store name is required.')
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
    </FormDrawer>
  )
}
