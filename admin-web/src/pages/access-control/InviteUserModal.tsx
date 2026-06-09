import { useMemo, useState, useEffect } from 'react'
import { UserPlus, MailCheck, ShieldCheck } from 'lucide-react'
import { useInviteUser } from '@/hooks/useAccessControl'
import { useSettings } from '@/hooks/useSettings'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import type { AccessRoles, AccessFranchise, InviteUserPayload } from '@/types/api'

interface Props {
  open: boolean
  onClose: () => void
  roles?: AccessRoles
  franchises?: AccessFranchise[]
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
  const settings = useSettings()
  const selfService = settings.data?.provisioning.mode === 'self_service'
  const emailEnabled = settings.data?.email.enabled ?? false
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
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={UserPlus}
      title="Invite user"
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Send invite"
      submittingLabel="Send invite"
      submitIcon={UserPlus}
      submitting={invite.isPending}
    >
      <div className="space-y-3">
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name">
            <input value={firstName} onChange={(e) => setFirstName(e.target.value)} className={drawerInputCls} placeholder="Priya" />
          </Field>
          <Field label="Last name">
            <input value={lastName} onChange={(e) => setLastName(e.target.value)} className={drawerInputCls} placeholder="Nair" />
          </Field>
        </div>
        <Field label="Email">
          <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" className={drawerInputCls} placeholder="priya@laundryghar.in" />
        </Field>
        <Field label="Role">
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)} className={drawerInputCls}>
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
            <select value={franchiseId} onChange={(e) => setFranchiseId(e.target.value)} className={drawerInputCls}>
              <option value="">Select a franchise…</option>
              {franchises?.map((f) => (
                <option key={f.id} value={f.id}>{f.name}</option>
              ))}
            </select>
          </Field>
        )}

        {/* What happens after inviting, driven by the provisioning setting */}
        <div className="flex items-start gap-2 rounded-lg bg-gray-50 px-3 py-2.5 text-xs text-gray-600">
          {selfService ? <MailCheck className="mt-0.5 h-4 w-4 shrink-0 text-lg-green" /> : <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-gray-400" />}
          <span>
            {selfService ? (
              <>The user gets an email with a secure link to set their own password and activate.</>
            ) : (
              <>The user is created as <span className="font-medium">Invited</span>. Activate them from the people list to set a temporary password.</>
            )}
            {!emailEnabled && (
              <> {' '}<span className="text-amber-600">Email is off — configure it in Settings → Email to deliver invites.</span></>
            )}
          </span>
        </div>
      </div>
    </FormDrawer>
  )
}
