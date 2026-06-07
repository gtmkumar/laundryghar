import { useMemo, useState, useEffect } from 'react'
import { X, Loader2, UserPlus } from 'lucide-react'
import { useInviteUser } from '@/hooks/useAccessControl'
import type { AccessRoles, AccessFranchises, InviteUserPayload } from '@/types/api'

interface Props {
  open: boolean
  onClose: () => void
  roles?: AccessRoles
  franchises?: AccessFranchises
}

function userTypeForRole(code: string, scopeType: string): string {
  if (code === 'platform_admin') return 'platform_admin'
  if (code === 'brand_admin') return 'brand_admin'
  if (code === 'franchise_owner') return 'franchise_owner'
  if (code === 'store_admin') return 'store_admin'
  if (scopeType === 'warehouse') return 'warehouse_staff'
  if (code === 'auditor') return 'auditor'
  return 'staff'
}

export function InviteUserModal({ open, onClose, roles, franchises }: Props) {
  const invite = useInviteUser()
  const allRoles = useMemo(() => roles?.groups.flatMap((g) => g.roles) ?? [], [roles])

  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [roleId, setRoleId] = useState('')
  const [franchiseId, setFranchiseId] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setFirstName(''); setLastName(''); setEmail(''); setRoleId(''); setFranchiseId(''); setError(null)
    }
  }, [open])

  if (!open) return null

  const role = allRoles.find((r) => r.id === roleId)
  const isFranchiseScoped = role ? role.scopeType !== 'platform' && role.scopeType !== 'brand' : false

  const submit = async () => {
    setError(null)
    if (!email.trim() || !role) {
      setError('Email and role are required.')
      return
    }
    if (isFranchiseScoped && !franchiseId) {
      setError('Pick a franchise for this role.')
      return
    }
    const payload: InviteUserPayload = {
      email: email.trim(),
      firstName: firstName.trim() || undefined,
      lastName: lastName.trim() || undefined,
      userType: userTypeForRole(role.code, role.scopeType),
      roleId: role.id,
      scopeType: isFranchiseScoped ? 'franchise' : 'brand',
      scopeId: isFranchiseScoped ? franchiseId : null,
    }
    try {
      await invite.mutateAsync(payload)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not invite user.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4" onClick={onClose}>
      <div
        className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
              <UserPlus className="h-4 w-4" />
            </span>
            <h2 className="text-lg font-bold text-gray-900">Invite user</h2>
          </div>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <Field label="First name">
              <input value={firstName} onChange={(e) => setFirstName(e.target.value)} className={inputCls} placeholder="Priya" />
            </Field>
            <Field label="Last name">
              <input value={lastName} onChange={(e) => setLastName(e.target.value)} className={inputCls} placeholder="Nair" />
            </Field>
          </div>
          <Field label="Email">
            <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" className={inputCls} placeholder="priya@laundryghar.in" />
          </Field>
          <Field label="Role">
            <select value={roleId} onChange={(e) => setRoleId(e.target.value)} className={inputCls}>
              <option value="">Select a role…</option>
              {roles?.groups.map((g) => (
                <optgroup key={g.tier} label={g.tierLabel}>
                  {g.roles.map((r) => (
                    <option key={r.id} value={r.id}>{r.name}</option>
                  ))}
                </optgroup>
              ))}
            </select>
          </Field>
          {isFranchiseScoped && (
            <Field label="Franchise">
              <select value={franchiseId} onChange={(e) => setFranchiseId(e.target.value)} className={inputCls}>
                <option value="">Select a franchise…</option>
                {franchises?.franchises.map((f) => (
                  <option key={f.id} value={f.id}>{f.name}</option>
                ))}
              </select>
            </Field>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onClose} className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={invite.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {invite.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Send invite
          </button>
        </div>
      </div>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}
