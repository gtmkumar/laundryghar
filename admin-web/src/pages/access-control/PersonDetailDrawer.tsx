import { useEffect, useState } from 'react'
import { X, Loader2, Pencil, Mail, Phone, Shield, MapPin, Clock, Check, Briefcase, CreditCard, IdCard, BadgeCheck } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUser, useUpdateUser, useChangeUserRole } from '@/hooks/useUsers'
import { useAccessRoles, useAccessFranchises } from '@/hooks/useAccessControl'
import { usePermissions } from '@/hooks/usePermissions'
import { statusTone } from './FranchiseTeamShared'
import type { UserEmploymentType, UserKycStatus } from '@/types/api'

export interface PersonSummary {
  id: string
  name: string
  roleName?: string
  scopeLabel?: string
  status: string
  initials?: string
}

interface Props {
  person: PersonSummary | null
  open: boolean
  onClose: () => void
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

const EMPLOYMENT_TYPES: { value: UserEmploymentType; label: string }[] = [
  { value: 'full_time', label: 'Full-time' },
  { value: 'part_time', label: 'Part-time' },
  { value: 'contractual', label: 'Contractual' },
  { value: 'consultant', label: 'Consultant' },
  { value: 'intern', label: 'Intern' },
]
const EMPLOYMENT_LABEL: Record<string, string> = Object.fromEntries(EMPLOYMENT_TYPES.map((e) => [e.value, e.label]))

const KYC_STATUSES: { value: UserKycStatus; label: string }[] = [
  { value: 'pending', label: 'Pending' },
  { value: 'verified', label: 'Verified' },
  { value: 'rejected', label: 'Rejected' },
]
const KYC_TONE: Record<string, string> = {
  verified: 'bg-emerald-100 text-emerald-700',
  pending: 'bg-amber-100 text-amber-700',
  rejected: 'bg-rose-100 text-rose-700',
}

function fmtDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })
}

/** View + edit a person: identity, employment, KYC docs, and bank/payout details.
 *  Status/role stay in the row ⋯ menu (they need privileged grant/revoke). */
export function PersonDetailDrawer({ person, open, onClose }: Props) {
  const { data: user, isLoading } = useUser(open ? person?.id ?? null : null)
  const update = useUpdateUser()
  const changeRole = useChangeUserRole()
  const { hasPermission } = usePermissions()
  const rolesQ = useAccessRoles()
  const franchisesQ = useAccessFranchises()
  const allRoles = rolesQ.data?.groups.flatMap((g) => g.roles) ?? []
  const franchises = franchisesQ.data?.pages.flatMap((p) => p.list) ?? []
  const canChangeRole = hasPermission('memberships.grant')

  const [roleEditing, setRoleEditing] = useState(false)
  const [newRoleId, setNewRoleId] = useState('')
  const [newFranchiseId, setNewFranchiseId] = useState('')
  const [roleErr, setRoleErr] = useState<string | null>(null)
  // After a successful change the `person` prop is stale, so show the new role locally.
  const [roleOverride, setRoleOverride] = useState<{ name: string; scope: string } | null>(null)

  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState({
    first: '', last: '', email: '', phone: '', designation: '',
    employmentType: '', pan: '', aadhaar: '', kycStatus: '',
    bankName: '', bankNumber: '', ifsc: '', upi: '',
  })
  const [err, setErr] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState<string | null>(null)

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }))

  useEffect(() => {
    if (user) {
      setForm({
        first: user.firstName ?? '', last: user.lastName ?? '', email: user.email ?? '',
        phone: user.phoneE164 ?? '', designation: user.designation ?? '',
        employmentType: user.employmentType ?? '', pan: user.panNumber ?? '',
        aadhaar: user.aadhaarNumberMasked ?? '', kycStatus: user.kycStatus ?? '',
        bankName: user.bankAccountName ?? '', bankNumber: user.bankAccountNumber ?? '',
        ifsc: user.bankIfsc ?? '', upi: user.upiId ?? '',
      })
    }
  }, [user])
  useEffect(() => {
    if (!open) {
      setEditing(false); setErr(null); setSavedAt(null)
      setRoleEditing(false); setRoleErr(null); setRoleOverride(null); setNewRoleId(''); setNewFranchiseId('')
    }
  }, [open])

  if (!open || !person) return null

  const tone = statusTone(person.status)
  const roleName = roleOverride?.name ?? person.roleName
  const scopeLabel = roleOverride?.scope ?? person.scopeLabel

  const selectedRole = allRoles.find((r) => r.id === newRoleId)
  const roleIsFranchiseScoped = selectedRole ? selectedRole.scopeType !== 'platform' && selectedRole.scopeType !== 'brand' : false

  const saveRole = async () => {
    setRoleErr(null)
    const role = allRoles.find((r) => r.id === newRoleId)
    if (!role) { setRoleErr('Pick a role.'); return }
    const franchiseScoped = role.scopeType !== 'platform' && role.scopeType !== 'brand'
    if (franchiseScoped && !newFranchiseId) { setRoleErr('Pick a franchise for this role.'); return }
    try {
      await changeRole.mutateAsync({
        id: person.id,
        payload: {
          roleId: role.id,
          scopeType: franchiseScoped ? 'franchise' : 'brand',
          scopeId: franchiseScoped ? newFranchiseId : null,
        },
      })
      setRoleOverride({ name: role.name, scope: franchiseScoped ? (franchises.find((f) => f.id === newFranchiseId)?.name ?? 'Franchise') : 'All / brand' })
      setRoleEditing(false); setNewRoleId(''); setNewFranchiseId('')
    } catch (e) {
      setRoleErr(e instanceof Error ? e.message : 'Could not change role.')
    }
  }

  const save = async () => {
    setErr(null)
    try {
      await update.mutateAsync({
        id: person.id,
        // Send trimmed values; empty string clears the field on the server.
        payload: {
          firstName: form.first.trim(), lastName: form.last.trim(),
          email: form.email.trim(), phone: form.phone.trim(), designation: form.designation.trim(),
          employmentType: form.employmentType, panNumber: form.pan.trim(),
          aadhaarNumberMasked: form.aadhaar.trim(), kycStatus: form.kycStatus,
          bankAccountName: form.bankName.trim(), bankAccountNumber: form.bankNumber.trim(),
          bankIfsc: form.ifsc.trim(), upiId: form.upi.trim(),
        },
      })
      setSavedAt(new Date().toLocaleTimeString())
      setEditing(false)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Could not save changes.')
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex justify-end bg-black/30" onClick={onClose}>
      <div className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
          <div className="flex min-w-0 items-center gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-lg-green text-sm font-semibold text-white">
              {person.initials ?? person.name.slice(0, 1).toUpperCase()}
            </span>
            <div className="min-w-0">
              <h2 className="truncate text-xl font-bold text-gray-900">{person.name}</h2>
              {person.roleName && <p className="truncate text-xs text-gray-400">{person.roleName}</p>}
            </div>
          </div>
          <div className="flex items-center gap-1">
            {!editing && !isLoading && (
              <button
                type="button"
                onClick={() => { setEditing(true); setSavedAt(null) }}
                className="inline-flex items-center gap-1 rounded-lg px-2.5 py-1.5 text-xs font-medium text-lg-green hover:bg-lg-green/10"
              >
                <Pencil className="h-3.5 w-3.5" /> Edit
              </button>
            )}
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
            >
              <X className="h-5 w-5" />
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
          {isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…
            </div>
          ) : (
            <>
              {/* Status + role + scope + KYC (read-only chips) */}
              <div className="flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 rounded-full bg-gray-50 px-2.5 py-1 text-xs font-medium capitalize">
                  <span className={cn('h-1.5 w-1.5 rounded-full', tone.dot)} />
                  <span className={tone.text}>{person.status}</span>
                </span>
                {roleName && (
                  <span className="rounded-full bg-lg-green/10 px-2.5 py-1 text-xs font-medium text-lg-green">{roleName}</span>
                )}
                {scopeLabel && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-gray-50 px-2.5 py-1 text-xs text-gray-500">
                    <MapPin className="h-3 w-3" />
                    {scopeLabel}
                  </span>
                )}
                {user?.kycStatus && (
                  <span className={cn('inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-medium capitalize', KYC_TONE[user.kycStatus])}>
                    <BadgeCheck className="h-3 w-3" /> KYC {user.kycStatus}
                  </span>
                )}
              </div>

              {/* Role & access — change a wrongly-assigned role (replace primary) */}
              <Section title="Role &amp; access">
                {roleEditing ? (
                  <div className="space-y-3">
                    <Field label="New role">
                      <select value={newRoleId} onChange={(e) => setNewRoleId(e.target.value)} className={inputCls}>
                        <option value="">Select a role…</option>
                        {rolesQ.data?.groups.map((g) => (
                          <optgroup key={g.tier} label={g.tierLabel}>
                            {g.roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                          </optgroup>
                        ))}
                      </select>
                    </Field>
                    {roleIsFranchiseScoped && (
                      <Field label="Franchise">
                        <select value={newFranchiseId} onChange={(e) => setNewFranchiseId(e.target.value)} className={inputCls}>
                          <option value="">Select a franchise…</option>
                          {franchises.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
                        </select>
                      </Field>
                    )}
                    {roleErr && <p className="text-sm text-red-600">{roleErr}</p>}
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={saveRole}
                        disabled={changeRole.isPending}
                        className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
                      >
                        {changeRole.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} Replace role
                      </button>
                      <button
                        type="button"
                        onClick={() => { setRoleEditing(false); setRoleErr(null) }}
                        className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
                      >
                        Cancel
                      </button>
                    </div>
                    <p className="text-xs text-gray-400">Revokes the current role and grants the new one as primary.</p>
                  </div>
                ) : (
                  <>
                    <dl className="space-y-2.5">
                      <DetailRow icon={<Shield className="h-4 w-4" />} label="Role" value={roleName ?? '—'} />
                      <DetailRow icon={<MapPin className="h-4 w-4" />} label="Scope" value={scopeLabel ?? '—'} />
                    </dl>
                    {roleOverride && <p className="mt-2 text-xs text-lg-green">Role updated.</p>}
                    {canChangeRole && (
                      <button
                        type="button"
                        onClick={() => { setRoleEditing(true); setNewRoleId(''); setNewFranchiseId(''); setRoleErr(null) }}
                        className="mt-2 inline-flex items-center gap-1 text-xs font-medium text-lg-green"
                      >
                        <Pencil className="h-3.5 w-3.5" /> Change role
                      </button>
                    )}
                  </>
                )}
              </Section>

              {/* Identity */}
              <Section title="Identity">
                {editing ? (
                  <div className="space-y-3">
                    <div className="grid grid-cols-2 gap-3">
                      <Field label="First name"><input value={form.first} onChange={set('first')} className={inputCls} /></Field>
                      <Field label="Last name"><input value={form.last} onChange={set('last')} className={inputCls} /></Field>
                    </div>
                    <Field label="Email"><input value={form.email} onChange={set('email')} className={inputCls} /></Field>
                    <Field label="Phone"><input value={form.phone} onChange={set('phone')} className={inputCls} placeholder="+91…" /></Field>
                  </div>
                ) : (
                  <dl className="space-y-2.5">
                    <DetailRow icon={<Mail className="h-4 w-4" />} label="Email" value={user?.email ?? '—'} />
                    <DetailRow icon={<Phone className="h-4 w-4" />} label="Phone" value={user?.phoneE164 ?? '—'} />
                    <DetailRow icon={<Shield className="h-4 w-4" />} label="Type" value={user?.userType ?? '—'} />
                  </dl>
                )}
              </Section>

              {/* Employment */}
              <Section title="Employment">
                {editing ? (
                  <div className="space-y-3">
                    <Field label="Employment type">
                      <select value={form.employmentType} onChange={set('employmentType')} className={inputCls}>
                        <option value="">Not set</option>
                        {EMPLOYMENT_TYPES.map((e) => <option key={e.value} value={e.value}>{e.label}</option>)}
                      </select>
                    </Field>
                    <Field label="Designation"><input value={form.designation} onChange={set('designation')} className={inputCls} placeholder="e.g. Store Supervisor" /></Field>
                  </div>
                ) : (
                  <dl className="space-y-2.5">
                    <DetailRow icon={<Briefcase className="h-4 w-4" />} label="Type" value={user?.employmentType ? EMPLOYMENT_LABEL[user.employmentType] ?? user.employmentType : '—'} />
                    <DetailRow icon={<IdCard className="h-4 w-4" />} label="Designation" value={user?.designation ?? '—'} />
                  </dl>
                )}
              </Section>

              {/* KYC / documents */}
              <Section title="KYC &amp; documents">
                {editing ? (
                  <div className="space-y-3">
                    <Field label="PAN"><input value={form.pan} onChange={set('pan')} className={cn(inputCls, 'uppercase')} placeholder="ABCDE1234F" maxLength={10} /></Field>
                    <Field label="Aadhaar (masked / last 4)"><input value={form.aadhaar} onChange={set('aadhaar')} className={inputCls} placeholder="XXXX XXXX 1234" /></Field>
                    <Field label="KYC status">
                      <select value={form.kycStatus} onChange={set('kycStatus')} className={inputCls}>
                        <option value="">Not started</option>
                        {KYC_STATUSES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
                      </select>
                    </Field>
                  </div>
                ) : (
                  <dl className="space-y-2.5">
                    <DetailRow icon={<IdCard className="h-4 w-4" />} label="PAN" value={user?.panNumber ?? '—'} />
                    <DetailRow icon={<IdCard className="h-4 w-4" />} label="Aadhaar" value={user?.aadhaarNumberMasked ?? '—'} />
                    <DetailRow
                      icon={<BadgeCheck className="h-4 w-4" />}
                      label="KYC"
                      value={user?.kycStatus ? `${user.kycStatus[0].toUpperCase()}${user.kycStatus.slice(1)}${user.kycVerifiedAt ? ` · ${fmtDate(user.kycVerifiedAt)}` : ''}` : 'Not started'}
                    />
                  </dl>
                )}
              </Section>

              {/* Bank / payout */}
              <Section title="Bank &amp; payout">
                {editing ? (
                  <div className="space-y-3">
                    <Field label="Account holder name"><input value={form.bankName} onChange={set('bankName')} className={inputCls} /></Field>
                    <Field label="Account number"><input value={form.bankNumber} onChange={set('bankNumber')} className={inputCls} /></Field>
                    <Field label="IFSC"><input value={form.ifsc} onChange={set('ifsc')} className={cn(inputCls, 'uppercase')} placeholder="HDFC0001234" maxLength={11} /></Field>
                    <Field label="UPI ID"><input value={form.upi} onChange={set('upi')} className={inputCls} placeholder="name@bank" /></Field>
                  </div>
                ) : (
                  <dl className="space-y-2.5">
                    <DetailRow icon={<CreditCard className="h-4 w-4" />} label="Account name" value={user?.bankAccountName ?? '—'} />
                    <DetailRow icon={<CreditCard className="h-4 w-4" />} label="Account no." value={user?.bankAccountNumber ?? '—'} />
                    <DetailRow icon={<CreditCard className="h-4 w-4" />} label="IFSC" value={user?.bankIfsc ?? '—'} />
                    <DetailRow icon={<CreditCard className="h-4 w-4" />} label="UPI" value={user?.upiId ?? '—'} />
                  </dl>
                )}
              </Section>

              {/* Account meta */}
              <Section title="Account">
                <dl className="space-y-2.5">
                  <DetailRow icon={<Clock className="h-4 w-4" />} label="Last active" value={fmtDate(user?.lastLoginAt)} />
                  <DetailRow icon={<Clock className="h-4 w-4" />} label="Member since" value={fmtDate(user?.createdAt)} />
                </dl>
                {!editing && <p className="mt-2 text-xs text-gray-400">Status changes (activate / suspend) are in the row’s ⋯ menu.</p>}
                {savedAt && !editing && <p className="mt-2 text-xs text-lg-green">Saved at {savedAt}</p>}
              </Section>
            </>
          )}
        </div>

        {/* Edit footer */}
        {editing && (
          <div className="border-t border-gray-100 px-6 py-4">
            {err && <p className="mb-2 text-sm text-red-600">{err}</p>}
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={save}
                disabled={update.isPending}
                className="inline-flex flex-1 items-center justify-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} Save changes
              </button>
              <button
                type="button"
                onClick={() => { setEditing(false); setErr(null) }}
                className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
              >
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-400">{title}</h3>
      {children}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}

function DetailRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-center gap-2 text-sm">
      <span className="text-gray-300">{icon}</span>
      <span className="text-gray-400">{label}</span>
      <span className="ml-auto truncate font-medium text-gray-700">{value}</span>
    </div>
  )
}
