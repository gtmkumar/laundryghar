import { useState } from 'react'
import { AlertCircle, Loader2, MapPin, Plus, Star, X } from 'lucide-react'
import { DetailSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { useGrantMembership, usePersonMemberships, useRevokeMembership } from '@/hooks/useAccessControl'
import { showToast } from '@/stores/toastStore'
import { apiErrorMessage } from '@/lib/apiError'
import type { AccessRoles, AccessFranchise, PersonMembershipDto } from '@/types/api'

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
 * change-role flow above.
 *
 * Audit finding A-4 ("blind writes") was that this panel could grant and revoke but never SHOW:
 * with no endpoint to list a person's memberships, it could only offer to revoke what the current
 * browser session had just granted, and anything granted yesterday was unreachable. It now reads
 * `GET /admin/access-control/people/{id}/memberships` and renders what the person actually holds.
 */
export function MembershipsPanel({
  personId, canGrant, canRevoke, isSelf, roles, franchises, effectiveBrandId,
}: Props) {
  const grant = useGrantMembership()
  const revoke = useRevokeMembership()
  // Skipped for the self case, where the whole panel renders a notice instead of any data.
  const memberships = usePersonMemberships(personId, !isSelf)

  const [roleId, setRoleId] = useState('')
  const [franchiseId, setFranchiseId] = useState('')
  const [isPrimary, setIsPrimary] = useState(false)
  const [err, setErr] = useState<string | null>(null)

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
      await grant.mutateAsync({
        userId: personId, roleId: role.id, scopeType, scopeId, isPrimary,
      })
      // No local copy is kept: the grant invalidates the memberships query, so the list below
      // re-reads from the server and shows the same rows anyone else would see.
      setRoleId(''); setFranchiseId(''); setIsPrimary(false)
      showToast('success', `Granted “${role.name}” membership.`)
    } catch (e) {
      setErr(apiErrorMessage(e, 'Could not grant the membership.'))
    }
  }

  const revokeOne = async (m: PersonMembershipDto) => {
    if (!window.confirm(`Revoke “${m.roleName}” at ${scopeLabelFor(m)}? The user loses this role at this scope.`)) return
    try {
      await revoke.mutateAsync({ userId: personId, payload: { membershipId: m.id } })
      showToast('success', 'Membership revoked.')
    } catch (e) {
      showToast('error', apiErrorMessage(e, 'Could not revoke the membership.'))
    }
  }

  // The server resolves the scope name; fall back to the local franchise list, then to the bare
  // scope type, so a row is never labelled with a raw uuid.
  const scopeLabelFor = (m: PersonMembershipDto): string => {
    if (m.scopeName) return m.scopeName
    if (m.scopeType === 'platform') return 'Platform'
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

          {/* What the person actually holds, read from the server (A-4). */}
          {memberships.isPending && (
            <p className="flex items-center gap-2 text-xs text-gray-500">
              <Loader2 className="h-3.5 w-3.5 animate-spin" /> Loading memberships…
            </p>
          )}

          {memberships.isError && (
            <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
              <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <div className="space-y-1">
                <p>{apiErrorMessage(memberships.error, 'Could not load this person’s memberships.')}</p>
                <button
                  type="button"
                  onClick={() => void memberships.refetch()}
                  className="font-medium underline underline-offset-2"
                >
                  Try again
                </button>
              </div>
            </div>
          )}

          {memberships.isSuccess && memberships.data.length === 0 && (
            <p className="text-xs text-gray-400">No additional memberships yet.</p>
          )}

          {memberships.isSuccess && memberships.data.length > 0 && (
            <ul className="space-y-1.5">
              {memberships.data.map((m) => (
                <li
                  key={m.id}
                  className="flex items-center gap-2 rounded-lg border border-gray-100 bg-white px-3 py-2 text-sm"
                >
                  <span className="font-medium text-gray-800">{m.roleName}</span>
                  {m.isPrimary && (
                    <span className="inline-flex items-center gap-0.5 rounded-full bg-lg-amber/20 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                      <Star className="h-3 w-3" /> Primary
                    </span>
                  )}
                  <span className="inline-flex items-center gap-1 text-xs text-gray-500">
                    <MapPin className="h-3 w-3" /> {scopeLabelFor(m)}
                  </span>
                  {m.expiresAt && (
                    <span className="text-[11px] text-gray-400">
                      until {new Date(m.expiresAt).toLocaleDateString()}
                    </span>
                  )}
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
        </div>
      )}
    </DetailSection>
  )
}
