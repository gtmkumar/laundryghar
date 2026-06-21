import { useState } from 'react'
import { Shield, Copy, Pencil } from 'lucide-react'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { useCreateRole, useUpdateRole, useCloneRole } from '@/hooks/useAccessControl'
import type { AccessRoleSummary } from '@/types/api'

export type RoleFormMode = 'create' | 'clone' | 'edit'

interface Props {
  open: boolean
  mode: RoleFormMode
  /** Source role for 'clone' / 'edit'. */
  source?: AccessRoleSummary | null
  onClose: () => void
  /** Called after a successful save; passes the new/cloned role (undefined for edit). */
  onSaved: (role?: AccessRoleSummary) => void
}

const SCOPES = [
  { value: 'brand', label: 'Brand (enterprise-wide)' },
  { value: 'franchise', label: 'Franchise' },
  { value: 'store', label: 'Store' },
  { value: 'warehouse', label: 'Warehouse' },
]

const MODE_META: Record<RoleFormMode, { title: string; icon: typeof Shield; submit: string }> = {
  create: { title: 'New custom role', icon: Shield, submit: 'Create role' },
  clone: { title: 'Clone role', icon: Copy, submit: 'Clone role' },
  edit: { title: 'Edit role', icon: Pencil, submit: 'Save changes' },
}

export function RoleFormModal({ open, mode, source, onClose, onSaved }: Props) {
  const create = useCreateRole()
  const update = useUpdateRole()
  const clone = useCloneRole()
  const meta = MODE_META[mode]

  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [scopeType, setScopeType] = useState('brand')
  const [error, setError] = useState<string | null>(null)

  // Seed fields each time the drawer (re)opens for a given mode/source.
  const [seed, setSeed] = useState('')
  const seedKey = `${open}|${mode}|${source?.id ?? ''}`
  if (open && seed !== seedKey) {
    setSeed(seedKey)
    setError(null)
    if (mode === 'edit' && source) {
      setCode(source.code); setName(source.name); setDescription(source.description ?? ''); setScopeType(source.scopeType)
    } else if (mode === 'clone' && source) {
      setCode(`${source.code}_copy`); setName(`${source.name} (copy)`); setDescription(source.description ?? ''); setScopeType(source.scopeType)
    } else {
      setCode(''); setName(''); setDescription(''); setScopeType('brand')
    }
  }

  if (!open) return null

  const submit = async () => {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return }
    if (mode !== 'edit' && !code.trim()) { setError('Code is required.'); return }
    try {
      if (mode === 'create') {
        const role = await create.mutateAsync({ code: code.trim(), name: name.trim(), description: description.trim() || null, scopeType })
        onSaved(role)
      } else if (mode === 'clone' && source) {
        const role = await clone.mutateAsync({ roleId: source.id, payload: { code: code.trim(), name: name.trim(), description: description.trim() || null } })
        onSaved(role)
      } else if (mode === 'edit' && source) {
        await update.mutateAsync({ roleId: source.id, payload: { name: name.trim(), description: description.trim() || null } })
        onSaved()
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the role.')
    }
  }

  const submitting = create.isPending || update.isPending || clone.isPending

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={meta.icon}
      title={meta.title}
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel={meta.submit}
      submittingLabel={meta.submit}
      submitIcon={meta.icon}
      submitting={submitting}
    >
      <div className="space-y-3">
        <Field label="Name">
          <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} placeholder="Regional Supervisor" />
        </Field>
        {mode === 'edit' ? (
          <Field label="Code">
            <input value={code} disabled className={`${drawerInputCls} opacity-60`} />
          </Field>
        ) : (
          <Field label="Code">
            <input
              value={code}
              onChange={(e) => setCode(e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_'))}
              className={drawerInputCls}
              placeholder="regional_supervisor"
            />
          </Field>
        )}
        <Field label="Description">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={drawerInputCls} placeholder="What this role is for" />
        </Field>
        {mode === 'create' && (
          <Field label="Scope">
            <select value={scopeType} onChange={(e) => setScopeType(e.target.value)} className={drawerInputCls}>
              {SCOPES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </Field>
        )}
        {mode === 'clone' && source && (
          <p className="rounded-lg bg-gray-50 px-3 py-2 text-xs text-gray-500">
            Copies all permissions from <span className="font-medium text-gray-700">{source.name}</span> into a new custom role you can then edit.
          </p>
        )}
      </div>
    </FormDrawer>
  )
}
