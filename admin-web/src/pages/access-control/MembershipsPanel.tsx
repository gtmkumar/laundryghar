import { useState } from 'react'
import { Loader2, Plus, X, Star, MapPin } from 'lucide-react'
import { DetailSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { useGrantMembership, useRevokeMembership } from '@/hooks/useAccessControl'
import { showToast } from '@/stores/toastStore'
import { apiErrorMessage } from '@/lib/apiError'
import type { AccessRoles, AccessFranchise, MembershipDto } from '@/types/api'

interface Props {
  personId: string
  canGrant: boolean
  canRevoke: boolean
  /** No one grants/revokes their OWN memberships — a self-escalation guard. */
  isSelf: boolean
  roles?: AccessRoles
  franchises: AccessFranchise[]
  /** The brand a brand-scoped membership binds to (JWT brand or platform-admin's active brand). */
  effectiveBrandId: string | null
}

/**
 * Additive, multi-scope memberships (docs/rbac.md §6) — distinct from the single-primary
 * change-role flow above. There is no backend endpoint to LIST a person's memberships, so
 * pre-existing ones can't be shown/revoked here; this grants NEW memberships and lets you
 * revoke the ones granted in this session (the grant response returns their ids).
 */
export function MembershipsPanel({
  personId, canGrant, canRevoke, isSelf, roles, franchises, effectiveBrandId,
}: Props) {
  const grant = useGrantMembership()
  const revoke = useRevokeMembership()

  const [roleId, setRoleId] = useState('')
  const [franchiseId, setFranchiseId] = useState('')
  const [isPrimary, setIsPrimary] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  // Memberships granted during this drawer session — the only ones we can revoke (we hold their ids).
  const [granted, setGranted] = useState<MembershipDto[]>([])

  const allRoles = roles?.groups.flatMap((g) => g.roles) ?? []
  const selectedRole = allRoles.find((r) => r.id === roleId)
  const franchiseScoped = selectedRole
    ? selectedRole.scopeType !== 'platform' && selectedRole.scopeType !== 'brand'
    : false

  if (!canGrant && !canRevoke) return null

  const submitGrant = async () => {
    setErr(null)
    const role = allRoles.find((r) => r.id === roleId)
    if (!role) { setErr('Pick a role.'); return }
    const fScoped = role.scopeType !== 'platform' && role.scopeType !== 'brand'
    if (fScoped && !franchiseId) { setErr('Pick a franchise for this role.'); return }

    // Bind to the correct scope id — mirrors the change-role flow so the issued token carries
    // the right brand_id (a null brand scope locks the user out of tenant-scoped services).
    const scopeType = role.scopeType === 'platform' ? 'platform' : fScoped ? 'franchise' : 'brand'
    let scopeId: string | null = null
    if (scopeType === 'brand') {
      if (!effectiveBrandId) { setErr('No active brand selected — pick a brand from the switcher first.'); return }
      scopeId = effectiveBrandId
    } else if (fScoped) {
      scopeId = franchiseId
    }

    try {
      const membership = await grant.mutateAsync({
        userId: personId, roleId: role.id, scopeType, scopeId, isPrimary,
      })
      setGranted((g) => [membership, ...g])
      setRoleId(''); setFranchiseId(''); setIsPrimary(false)
      showToast('success', `Granted “${role.name}” membership.`)
    } catch (e) {
      setErr(apiErrorMessage(e, 'Could not grant the membership.'))
    }
  }

  const revokeOne = async (m: MembershipDto) => {
    if (!window.confirm('Revoke this membership? The user loses this role at this scope.')) return
    try {
      await revoke.mutateAsync({ userId: personId, payload: { membershipId: m.id } })
      setGranted((g) => g.filter((x) => x.id !== m.id))
      showToast('success', 'Membership revoked.')
    } catch (e) {
      showToast('error', apiErrorMessage(e, 'Could not revoke the membership.'))
    }
  }

  const scopeLabelFor = (m: MembershipDto): string => {
    if (m.scopeType === 'platform') return 'Platform'
    if (m.scopeType === 'brand') return 'Brand'
    const f = franchises.find((x) => x.id === m.scopeId)
    return f ? f.name : `${m.scopeType[0].toUpperCase()}${m.scopeType.slice(1)}`
  }

  return (
    <DetailSection plain title="Additional memberships">
      {isSelf ? (
        <p className="text-xs text-gray-400">You can’t change your own memberships.</p>
      ) : (
        <div className="space-y-3">
          <p className="text-xs text-gray-500">
            Grant extra roles at other scopes on top of the primary role above (e.g. an ops lead who also
            manages one franchise). This is additive — it doesn’t replace the primary role.
          </p>

          {/* Session-granted memberships (revocable — we hold their ids) */}
          {granted.length > 0 && (
            <ul className="space-y-1.5">
              {granted.map((m) => (
                <li
                  key={m.id}
                  className="flex items-center gap-2 rounded-lg border border-gray-100 bg-white px-3 py-2 text-sm"
                >
                  <span className="font-medium text-gray-800">{m.roleCode}</span>
                  {m.isPrimary && (
                    <span className="inline-flex items-center gap-0.5 rounded-full bg-lg-amber/20 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                      <Star className="h-3 w-3" /> Primary
                    </span>
                  )}
                  <span className="inline-flex items-center gap-1 text-xs text-gray-500">
                    <MapPin className="h-3 w-3" /> {scopeLabelFor(m)}
                  </span>
                  {canRevoke && (
                    <button
                      type="button"
                      onClick={() => revokeOne(m)}
                      disabled={revoke.isPending}
                      title="Revoke this membership"
                      className="ml-auto inline-flex items-center gap-1 rounded-md border border-red-200 px-2 py-1 text-xs font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
                    >
                      <X className="h-3.5 w-3.5" /> Revoke
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}

          {canGrant && (
            <div className="space-y-3">
              <Field label="Role">
                <select value={roleId} onChange={(e) => setRoleId(e.target.value)} className={drawerInputCls}>
                  <option value="">Select a role…</option>
                  {roles?.groups.map((g) => (
                    <optgroup key={g.tier} label={g.tierLabel}>
                      {g.roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                    </optgroup>
                  ))}
                </select>
              </Field>
              {franchiseScoped && (
                <Field label="Franchise">
                  <select value={franchiseId} onChange={(e) => setFranchiseId(e.target.value)} className={drawerInputCls}>
                    <option value="">Select a franchise…</option>
                    {franchises.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
                  </select>
                </Field>
              )}
              <label className="flex items-center gap-2 text-sm text-gray-600">
                <input
                  type="checkbox"
                  checked={isPrimary}
                  onChange={(e) => setIsPrimary(e.target.checked)}
                  className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green"
                />
                Make this the primary membership
              </label>

              {err && <p className="text-sm text-red-600">{err}</p>}

              <button
                type="button"
                onClick={submitGrant}
                disabled={grant.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {grant.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
                Grant membership
              </button>
            </div>
          )}

          <p className="text-xs text-gray-400">
            Existing memberships aren’t listed here yet — the API has no per-user memberships read endpoint,
            so only memberships granted in this session can be revoked.
          </p>
        </div>
      )}
    </DetailSection>
  )
}
