import { useMemo, useState } from 'react'
import { AlertCircle, Check, Loader2, MapPin, ShieldOff, SlidersHorizontal, Trash2 } from 'lucide-react'
import { DetailSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import {
  usePermissionCatalog,
  usePersonPermissionOverrides,
  useSetUserPermissionOverride,
} from '@/hooks/useAccessControl'
import { showToast } from '@/stores/toastStore'
import { apiErrorMessage } from '@/lib/apiError'
import type { PersonPermissionOverrideDto, SetUserPermissionOverridePayload } from '@/types/api'

interface Props {
  personId: string
  /** The gating permission the caller holds (permissions.assign). When false the panel is hidden upstream. */
  canManage: boolean
  /** No one edits their OWN overrides — a self-escalation guard mirroring the change-role flow. */
  isSelf: boolean
}

type Effect = 'allow' | 'deny'

// Scope options offered in the picker. "" = a global override (scopeType null on the wire).
// Platform scope is intentionally omitted — a global override already means "everywhere".
const SCOPE_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'Global (everywhere)' },
  { value: 'brand', label: 'Brand' },
  { value: 'franchise', label: 'Franchise' },
  { value: 'store', label: 'Store' },
  { value: 'warehouse', label: 'Warehouse' },
]

/**
 * Per-user permission overrides (docs/rbac.md §7): pick a permission, allow or deny it
 * (deny = "suspend this capability for this one user"), optionally scope it to a subtree /
 * time-box it / add a reason, then Apply — or Clear to remove the override.
 *
 * Audit finding A-4 ("blind writes") was that this panel could set and clear but never SHOW, so the
 * only way to answer "what exceptions does this person carry" was to open the database. The ABAC
 * plan had named the same gap independently as task A8.2. It now reads
 * `GET /admin/access-control/people/{id}/permission-overrides` and lists what is actually in force.
 */
export function PermissionOverridesPanel({ personId, canManage, isSelf }: Props) {
  const catalogQ = usePermissionCatalog(canManage && !isSelf)
  const overridesQ = usePersonPermissionOverrides(personId, canManage && !isSelf)
  const setOverride = useSetUserPermissionOverride()

  const [permCode, setPermCode] = useState('')
  const [effect, setEffect] = useState<Effect>('deny')
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [scopeType, setScopeType] = useState('')
  const [scopeId, setScopeId] = useState('')
  const [expiresAt, setExpiresAt] = useState('')
  const [reason, setReason] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)

  // Group the catalog by module for the <optgroup>s. Falls back to a free-text code input
  // when the catalog can't be loaded (empty / errored / forbidden) — a code is all the API needs.
  const grouped = useMemo(() => {
    const map = new Map<string, { code: string; name: string }[]>()
    for (const p of catalogQ.data ?? []) {
      const list = map.get(p.module) ?? []
      list.push({ code: p.code, name: p.name })
      map.set(p.module, list)
    }
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b))
  }, [catalogQ.data])
  const useTextInput = !catalogQ.isLoading && (catalogQ.isError || (catalogQ.data?.length ?? 0) === 0)

  if (!canManage) return null

  const validate = (): string | null => {
    if (!permCode.trim()) return 'Pick a permission.'
    if (scopeType && !scopeId.trim()) return 'Enter a scope id for the chosen scope, or set scope to Global.'
    return null
  }

  /** Load a listed override back into the form — makes clearing or amending one a single click
   *  instead of retyping its code and scope by hand. */
  const editExisting = (o: PersonPermissionOverrideDto) => {
    setErr(null); setDone(null)
    setPermCode(o.permissionCode)
    setEffect(o.effect === 'allow' ? 'allow' : 'deny')
    setScopeType(o.scopeType ?? '')
    setScopeId(o.scopeId ?? '')
    setReason(o.reason ?? '')
    setExpiresAt(o.expiresAt ? o.expiresAt.slice(0, 16) : '')
    if (o.scopeType || o.expiresAt || o.reason) setShowAdvanced(true)
  }

  const submit = async (clearing: boolean) => {
    setErr(null)
    setDone(null)
    const problem = clearing ? (permCode.trim() ? null : 'Pick a permission to clear.') : validate()
    if (problem) { setErr(problem); return }

    const payload: SetUserPermissionOverridePayload = {
      permissionCode: permCode.trim(),
      effect: clearing ? null : effect,
      scopeType: scopeType || undefined,
      scopeId: scopeType && scopeId.trim() ? scopeId.trim() : undefined,
      reason: !clearing && reason.trim() ? reason.trim() : undefined,
      expiresAt: !clearing && expiresAt ? new Date(expiresAt).toISOString() : undefined,
    }
    try {
      await setOverride.mutateAsync({ personId, payload })
      const label = clearing ? 'cleared' : `set to ${effect}`
      setDone(`Override for “${permCode.trim()}” ${label}.`)
      showToast('success', `Permission override ${label} for “${permCode.trim()}”.`)
    } catch (e) {
      setErr(apiErrorMessage(e, 'Could not update the override.'))
    }
  }

  return (
    <DetailSection plain title="Permission overrides">
      {isSelf ? (
        <p className="text-xs text-gray-400">You can’t change your own permission overrides.</p>
      ) : (
        <div className="space-y-3">
          <p className="text-xs text-gray-500">
            Grant (allow) or suspend (deny) a single capability for this person only — on top of their role.
            Leave scope Global to apply everywhere.
          </p>

          <Field label="Permission">
            {useTextInput ? (
              <input
                value={permCode}
                onChange={(e) => setPermCode(e.target.value)}
                className={drawerInputCls}
                placeholder="e.g. orders.refund"
                spellCheck={false}
              />
            ) : (
              <select
                value={permCode}
                onChange={(e) => setPermCode(e.target.value)}
                disabled={catalogQ.isLoading}
                className={drawerInputCls}
              >
                <option value="">{catalogQ.isLoading ? 'Loading permissions…' : 'Select a permission…'}</option>
                {grouped.map(([module, perms]) => (
                  <optgroup key={module} label={module}>
                    {perms.map((p) => (
                      <option key={p.code} value={p.code}>
                        {p.name} ({p.code})
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            )}
          </Field>

          <Field label="Effect">
            <div className="flex gap-2">
              {(['allow', 'deny'] as Effect[]).map((e) => (
                <button
                  key={e}
                  type="button"
                  onClick={() => setEffect(e)}
                  className={
                    'flex-1 rounded-lg border px-3 py-2 text-sm font-medium capitalize transition-colors ' +
                    (effect === e
                      ? e === 'deny'
                        ? 'border-rose-300 bg-rose-50 text-rose-700'
                        : 'border-lg-green bg-lg-green/10 text-lg-green'
                      : 'border-gray-200 text-gray-600 hover:bg-gray-50')
                  }
                >
                  {e === 'deny' ? 'Deny (suspend)' : 'Allow'}
                </button>
              ))}
            </div>
          </Field>

          <button
            type="button"
            onClick={() => setShowAdvanced((v) => !v)}
            className="inline-flex items-center gap-1 text-xs font-medium text-lg-green"
          >
            <SlidersHorizontal className="h-3.5 w-3.5" />
            {showAdvanced ? 'Hide' : 'Add'} scope, expiry or reason (optional)
          </button>

          {showAdvanced && (
            <div className="space-y-3 rounded-lg border border-gray-100 bg-gray-50/50 p-3">
              <Field label="Scope">
                <select value={scopeType} onChange={(e) => setScopeType(e.target.value)} className={drawerInputCls}>
                  {SCOPE_OPTIONS.map((s) => (
                    <option key={s.value || 'global'} value={s.value}>{s.label}</option>
                  ))}
                </select>
              </Field>
              {scopeType && (
                <Field label="Scope id" hint="The id of the brand / franchise / store / warehouse to confine this override to.">
                  <input
                    value={scopeId}
                    onChange={(e) => setScopeId(e.target.value)}
                    className={drawerInputCls}
                    placeholder="00000000-0000-0000-0000-000000000000"
                    spellCheck={false}
                  />
                </Field>
              )}
              <Field label="Expires at" hint="Leave empty for no expiry.">
                <input
                  type="datetime-local"
                  value={expiresAt}
                  onChange={(e) => setExpiresAt(e.target.value)}
                  className={drawerInputCls}
                />
              </Field>
              <Field label="Reason" hint="Recorded in the audit trail.">
                <input
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  className={drawerInputCls}
                  placeholder="Why this override?"
                />
              </Field>
            </div>
          )}

          {err && <p className="text-sm text-red-600">{err}</p>}
          {done && <p className="text-xs text-lg-green">{done}</p>}

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => submit(false)}
              disabled={setOverride.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              {setOverride.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : effect === 'deny' ? <ShieldOff className="h-4 w-4" /> : <Check className="h-4 w-4" />}
              Apply override
            </button>
            <button
              type="button"
              onClick={() => submit(true)}
              disabled={setOverride.isPending}
              title="Remove this override (revert to role-derived behaviour)"
              className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
            >
              <Trash2 className="h-3.5 w-3.5" /> Clear
            </button>
          </div>
          {/* What is actually in force (A-4). */}
          <div className="space-y-1.5 border-t border-gray-100 pt-3">
            <p className="text-[11px] font-medium uppercase tracking-wide text-gray-400">
              Overrides in force
            </p>

            {overridesQ.isPending && (
              <p className="flex items-center gap-2 text-xs text-gray-500">
                <Loader2 className="h-3.5 w-3.5 animate-spin" /> Loading overrides…
              </p>
            )}

            {overridesQ.isError && (
              <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
                <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                <div className="space-y-1">
                  <p>{apiErrorMessage(overridesQ.error, 'Could not load this person’s overrides.')}</p>
                  <button
                    type="button"
                    onClick={() => void overridesQ.refetch()}
                    className="font-medium underline underline-offset-2"
                  >
                    Try again
                  </button>
                </div>
              </div>
            )}

            {overridesQ.isSuccess && overridesQ.data.length === 0 && (
              <p className="text-xs text-gray-400">
                None — this person’s access comes entirely from their roles.
              </p>
            )}

            {overridesQ.isSuccess && overridesQ.data.length > 0 && (
              <ul className="space-y-1.5">
                {overridesQ.data.map((o) => (
                  <li
                    key={o.id}
                    className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-lg border border-gray-100 bg-white px-3 py-2 text-sm"
                  >
                    <span
                      className={
                        o.effect === 'deny'
                          ? 'inline-flex items-center gap-1 rounded-full bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-700'
                          : 'inline-flex items-center gap-1 rounded-full bg-green-50 px-1.5 py-0.5 text-[11px] font-semibold text-green-700'
                      }
                    >
                      {o.effect === 'deny' ? <ShieldOff className="h-3 w-3" /> : <Check className="h-3 w-3" />}
                      {o.effect === 'deny' ? 'Denied' : 'Allowed'}
                    </span>
                    <span className="font-medium text-gray-800">{o.permissionName}</span>
                    <code className="rounded bg-gray-50 px-1 text-[11px] text-gray-500">{o.permissionCode}</code>
                    {o.scopeType && (
                      <span className="inline-flex items-center gap-1 text-xs text-gray-500">
                        <MapPin className="h-3 w-3" /> {o.scopeName ?? o.scopeType}
                      </span>
                    )}
                    {o.expiresAt && (
                      <span className="text-[11px] text-gray-400">
                        until {new Date(o.expiresAt).toLocaleDateString()}
                      </span>
                    )}
                    {o.reason && <span className="w-full text-[11px] text-gray-400">“{o.reason}”</span>}
                    <button
                      type="button"
                      onClick={() => editExisting(o)}
                      className="ml-auto rounded-md border border-gray-200 px-2 py-1 text-xs font-medium text-gray-600 hover:bg-gray-50"
                    >
                      Edit / clear
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <p className="text-xs text-gray-400">
            Applying or clearing takes effect immediately and re-issues the user’s tokens.
          </p>
        </div>
      )}
    </DetailSection>
  )
}
