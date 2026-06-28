import { useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Loader2, Check, Minus, Copy, Plus, Pencil, Trash2, Lock, ShieldAlert } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useSetRoleCells, useDeleteRole } from '@/hooks/useAccessControl'
import { useBrandEntitlements } from '@/hooks/useEntitlements'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import { ForbiddenState, isForbiddenError } from '@/components/shared/ForbiddenState'
import { useActiveVertical } from '@/hooks/useActiveVertical'
import { scopeScopedLabel } from '@/lib/verticalTerms'
import { RoleFormModal, type RoleFormMode } from './RoleFormModal'
import type { AccessRoles, AccessRoleSummary } from '@/types/api'

interface Props {
  query: { data?: AccessRoles; isLoading: boolean; isError: boolean; error?: unknown }
  search?: string
}

// Soft badge per vertical, shown on vertical-specific system roles (e.g. in the platform-admin
// all-verticals view); neutral roles carry no badge.
const VERTICAL_BADGE: Record<string, string> = {
  laundry: 'bg-sky-100 text-sky-700',
  salon: 'bg-pink-100 text-pink-700',
  logistics: 'bg-amber-100 text-amber-700',
}

export function RolesTab({ query, search }: Props) {
  const { data, isLoading, isError, error } = query
  const saveCells = useSetRoleCells()
  const deleteRole = useDeleteRole()
  const qc = useQueryClient()
  const vertical = useActiveVertical()
  const { hasPermission } = usePermissions()
  const canEdit = hasPermission('permissions.assign')
  const canManageRoles = hasPermission('roles.manage')

  // Role create/clone/edit modal state.
  const [roleModal, setRoleModal] = useState<{ mode: RoleFormMode; source: AccessRoleSummary | null } | null>(null)

  // Brand entitlement → which navigator modules the active brand has licensed. Used to
  // grey out matrix rows for unlicensed modules (their permissions are moot). Fails open:
  // if entitlements aren't available (no saas.read / no brand), nothing is greyed.
  const entitlements = useBrandEntitlements()
  const entByKey = useMemo(() => {
    if (!entitlements.data) return null
    return new Map(entitlements.data.modules.map((m) => [m.key, m.entitled]))
  }, [entitlements.data])
  const isLicensed = (key: string) => entByKey == null || entByKey.get(key) !== false

  // Roles arrive fully loaded (no pagination), so filtering client-side is complete.
  const term = (search ?? '').trim().toLowerCase()
  const groups = useMemo(() => {
    const gs = data?.groups ?? []
    if (!term) return gs
    return gs
      .map((g) => ({
        ...g,
        roles: g.roles.filter(
          (r) => r.name.toLowerCase().includes(term) || (r.description ?? '').toLowerCase().includes(term),
        ),
      }))
      .filter((g) => g.roles.length > 0)
  }, [data, term])

  const allRoles = useMemo(() => groups.flatMap((g) => g.roles), [groups])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const selected: AccessRoleSummary | undefined =
    allRoles.find((r) => r.id === selectedId) ?? allRoles[0]

  // Draft cell set for the selected role (edit locally, then Save).
  const [draft, setDraft] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  // Reseed the draft when the selected role — or its persisted cells — change.
  const seedSig = selected ? `${selected.id}|${selected.onCells.join(',')}` : null
  const [draftSig, setDraftSig] = useState<string | null>(null)
  if (selected && draftSig !== seedSig) {
    setDraftSig(seedSig)
    setDraft(new Set(selected.onCells))
  }

  // Dirty computed before the early returns so the unsaved-changes hook stays unconditional.
  const original = new Set(selected?.onCells ?? [])
  const dirty = !!selected && (draft.size !== original.size || [...draft].some((c) => !original.has(c)))

  // Warn on browser close / hard navigation while there are unsaved matrix edits.
  useEffect(() => {
    if (!dirty) return
    const handler = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [dirty])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading roles…
      </div>
    )
  }
  if (isError || !data) {
    if (isForbiddenError(error)) return <ForbiddenState message="You don’t have access to roles and permissions." />
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load roles.</div>
  }
  if (!selected) {
    return <div className="py-24 text-center text-sm text-gray-400">No roles match “{search}”.</div>
  }

  // Guard against silently discarding unsaved matrix edits when switching roles.
  const selectRole = (id: string) => {
    if (id === selected.id) return
    if (dirty && !window.confirm('Discard unsaved permission changes?')) return
    setSelectedId(id)
  }

  // After create/clone, jump to the new role; after delete, fall back to the first role.
  const onRoleSaved = (role?: AccessRoleSummary) => {
    setRoleModal(null)
    if (role) setSelectedId(role.id)
  }
  const removeRole = async () => {
    if (selected.isSystem) return
    if (selected.memberCount > 0) {
      showToast('error', `“${selected.name}” has ${selected.memberCount} member(s) — reassign them before deleting.`)
      return
    }
    if (!window.confirm(`Delete the role “${selected.name}”? This can't be undone.`)) return
    try {
      await deleteRole.mutateAsync(selected.id)
      setSelectedId(null)
      showToast('success', `Deleted role “${selected.name}”.`)
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not delete the role.')
    }
  }

  // View dependency: a write action implies View; clearing View clears the module's row
  // (edit/create/… are meaningless without view). Keeps grants internally consistent.
  const applyDeps = (set: Set<string>, moduleKey: string, action: string, on: boolean) => {
    if (on) {
      if (action !== 'view') set.add(`${moduleKey}:view`)
    } else if (action === 'view') {
      for (const a of data.actions) set.delete(`${moduleKey}:${a}`)
    }
  }

  const toggle = (cellKey: string) => {
    if (!canEdit) return
    const [mod, act] = cellKey.split(':')
    setDraft((prev) => {
      const next = new Set(prev)
      const turnOn = !next.has(cellKey)
      if (turnOn) next.add(cellKey)
      else next.delete(cellKey)
      applyDeps(next, mod, act, turnOn)
      return next
    })
  }

  // ── Bulk select (P1-1): set many cells at once, only for licensed modules ──
  const setMany = (keys: string[], on: boolean) => {
    if (!canEdit) return
    setDraft((prev) => {
      const next = new Set(prev)
      for (const k of keys) {
        const [mod, act] = k.split(':')
        if (on) next.add(k)
        else next.delete(k)
        applyDeps(next, mod, act, on)
      }
      return next
    })
  }
  const licensedModules = data.modules.filter((m) => isLicensed(m.key))
  const triState = (keys: string[]): 'all' | 'some' | 'none' => {
    const on = keys.filter((k) => draft.has(k)).length
    return on === 0 ? 'none' : on === keys.length ? 'all' : 'some'
  }
  const colKeys = (a: string) => licensedModules.map((m) => `${m.key}:${a}`)
  const rowKeys = (mkey: string) => data.actions.map((a) => `${mkey}:${a}`)

  const save = async () => {
    if (!selected) return
    const changes = [] as { cellKey: string; enabled: boolean }[]
    for (const m of data.modules)
      for (const a of data.actions) {
        const key = `${m.key}:${a}`
        if (original.has(key) !== draft.has(key)) changes.push({ cellKey: key, enabled: draft.has(key) })
      }
    if (changes.length === 0) return

    setSaving(true)
    try {
      // Single atomic request — all changes apply together or none do.
      await saveCells.mutateAsync({ roleId: selected.id, changes })
      showToast('success', `Saved ${changes.length} change${changes.length > 1 ? 's' : ''} for ${selected.name}.`)
    } catch (e) {
      await qc.invalidateQueries({ queryKey: ['access', 'roles'] }) // reconcile to true state
      showToast('error', e instanceof Error ? e.message : 'Could not save changes — nothing was changed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[300px_1fr]">
      {/* Left: role list */}
      <div className="rounded-2xl border border-gray-200 bg-white p-3">
        {groups.map((g) => (
          <div key={g.tier} className="mb-3">
            <p className="px-2 py-1.5 text-[10px] font-semibold uppercase tracking-widest text-gray-400">
              {g.tierLabel}
            </p>
            <div className="space-y-0.5">
              {g.roles.map((r) => {
                const active = r.id === selected.id
                return (
                  <button
                    key={r.id}
                    type="button"
                    onClick={() => selectRole(r.id)}
                    className={cn(
                      'w-full rounded-xl px-3 py-2 text-left transition-colors',
                      active ? 'bg-lg-green/8 ring-1 ring-lg-green/30' : 'hover:bg-gray-50',
                    )}
                  >
                    <div className="flex items-center gap-2">
                      <span className={cn('h-1.5 w-1.5 rounded-full', active ? 'bg-lg-green' : 'bg-gray-300')} />
                      <span className={cn('flex-1 text-sm font-medium', active ? 'text-lg-green' : 'text-gray-800')}>
                        {r.name}
                      </span>
                      <span className="rounded-full bg-gray-100 px-1.5 text-[11px] font-semibold text-gray-500">
                        {r.memberCount}
                      </span>
                    </div>
                    {r.description && (
                      <p className="mt-0.5 pl-3.5 text-xs text-gray-400 line-clamp-1">{r.description}</p>
                    )}
                  </button>
                )
              })}
            </div>
          </div>
        ))}
        {canManageRoles && (
          <button
            type="button"
            onClick={() => setRoleModal({ mode: 'create', source: null })}
            className="mt-1 flex w-full items-center justify-center gap-1.5 rounded-xl border border-dashed border-gray-300 py-2 text-xs font-medium text-lg-green hover:bg-lg-green/5"
          >
            <Plus className="h-3.5 w-3.5" /> New custom role
          </button>
        )}
      </div>

      {/* Right: permission matrix */}
      <div className="rounded-2xl border border-gray-200 bg-white">
        <div className="flex flex-wrap items-center gap-3 border-b border-gray-100 px-5 py-4">
          <div>
            <div className="flex items-center gap-2">
              <h3 className="text-lg font-bold text-gray-900">{selected.name}</h3>
              <span className="rounded-full bg-lg-green/10 px-2 py-0.5 text-xs font-medium text-lg-green">
                {scopeScopedLabel(selected.scopeType, vertical)}
              </span>
              {selected.verticalKey && (
                <span className={cn('rounded-full px-2 py-0.5 text-xs font-medium capitalize', VERTICAL_BADGE[selected.verticalKey] ?? 'bg-slate-100 text-slate-600')}>
                  {selected.verticalKey}
                </span>
              )}
            </div>
            {selected.description && <p className="mt-0.5 text-sm text-gray-500">{selected.description}</p>}
            {entByKey && (() => {
              const n = data.modules.filter((m) => entByKey.get(m.key) === false).length
              return n > 0 ? (
                <p className="mt-1 inline-flex items-center gap-1 text-xs text-amber-700">
                  <Lock className="h-3 w-3" /> {n} module{n > 1 ? 's' : ''} not licensed for this brand — manage under Licensing
                </p>
              ) : null
            })()}
          </div>
          <div className="ml-auto flex items-center gap-2">
            {canManageRoles && !selected.isSystem && (
              <button
                type="button"
                onClick={() => setRoleModal({ mode: 'edit', source: selected })}
                title="Edit role name & description"
                className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
              >
                <Pencil className="h-3.5 w-3.5" /> Edit
              </button>
            )}
            {canManageRoles && (
              <button
                type="button"
                onClick={() => setRoleModal({ mode: 'clone', source: selected })}
                title="Create a new custom role from this one"
                className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
              >
                <Copy className="h-3.5 w-3.5" /> Clone
              </button>
            )}
            {canManageRoles && !selected.isSystem && (
              <button
                type="button"
                onClick={removeRole}
                disabled={deleteRole.isPending}
                title={selected.memberCount > 0 ? 'Reassign members before deleting' : 'Delete this custom role'}
                className="inline-flex items-center gap-1.5 rounded-lg border border-red-200 px-3 py-1.5 text-sm font-medium text-red-600 hover:bg-red-50 disabled:opacity-60"
              >
                <Trash2 className="h-3.5 w-3.5" /> Delete
              </button>
            )}
            {canEdit && (
              <button
                type="button"
                onClick={save}
                disabled={!dirty || saving}
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-sm font-semibold transition-colors',
                  dirty && !saving ? 'bg-lg-green text-white hover:bg-[var(--lg-green-hover)]' : 'bg-gray-100 text-gray-400',
                )}
              >
                {saving && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Save changes
              </button>
            )}
            {!canEdit && (
              <span className="text-xs text-gray-400">Read-only — you don’t have permission to edit roles.</span>
            )}
          </div>
        </div>

        {/* Built-in role notice (P0-3): editable, but changes affect everyone with the role. */}
        {selected.isSystem && (
          <div className="flex items-center gap-2 border-b border-amber-100 bg-amber-50/60 px-5 py-2.5 text-xs text-amber-800">
            <ShieldAlert className="h-4 w-4 shrink-0" />
            Built-in role — changes apply to everyone assigned this role across the platform. Edit with care.
          </div>
        )}

        {/* Matrix — scrolls within a bounded height so headers can stick */}
        <div className="max-h-[70vh] overflow-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-gray-500">
                <th scope="col" className="sticky left-0 top-0 z-30 bg-white px-5 py-3">Module</th>
                {data.actions.map((a) => {
                  const ks = colKeys(a)
                  const st = triState(ks)
                  return (
                    <th key={a} scope="col" className="sticky top-0 z-20 bg-white px-3 py-3 text-center">
                      {canEdit && ks.length > 0 ? (
                        <button
                          type="button"
                          onClick={() => setMany(ks, st !== 'all')}
                          aria-label={`${st === 'all' ? 'Clear' : 'Grant'} ${a} on all licensed modules`}
                          title={`${st === 'all' ? 'Clear' : 'Grant'} ${a} on all licensed modules`}
                          className="mx-auto inline-flex items-center gap-1 rounded px-1.5 py-0.5 uppercase hover:bg-gray-50"
                        >
                          <span className={cn(
                            'flex h-3.5 w-3.5 items-center justify-center rounded-sm border',
                            st === 'all' ? 'border-lg-green bg-lg-green text-white'
                              : st === 'some' ? 'border-lg-green bg-lg-green/30 text-lg-green'
                              : 'border-gray-300 text-transparent',
                          )}>
                            {st === 'all' ? <Check className="h-2.5 w-2.5" strokeWidth={3} />
                              : st === 'some' ? <Minus className="h-2.5 w-2.5" strokeWidth={3} /> : null}
                          </span>
                          {a}
                        </button>
                      ) : a}
                    </th>
                  )
                })}
              </tr>
            </thead>
            <tbody>
              {data.modules.map((m) => {
                const licensed = isLicensed(m.key)
                return (
                  <tr key={m.key} className={cn('border-t border-gray-50', !licensed && 'bg-gray-50/40')}>
                    <th
                      scope="row"
                      className={cn('sticky left-0 z-10 px-5 py-2.5 text-left font-medium',
                        licensed ? 'bg-white text-gray-700' : 'bg-gray-50 text-gray-400')}
                    >
                      <span className="inline-flex items-center gap-1.5">
                        {licensed && canEdit && (() => {
                          const ks = rowKeys(m.key)
                          const st = triState(ks)
                          return (
                            <button
                              type="button"
                              onClick={() => setMany(ks, st !== 'all')}
                              aria-label={`${st === 'all' ? 'Clear' : 'Grant'} all permissions for ${m.label}`}
                              title={`${st === 'all' ? 'Clear' : 'Grant all'} for ${m.label}`}
                              className={cn(
                                'flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded-sm border',
                                st === 'all' ? 'border-lg-green bg-lg-green text-white'
                                  : st === 'some' ? 'border-lg-green bg-lg-green/30 text-lg-green'
                                  : 'border-gray-300 text-transparent hover:border-gray-400',
                              )}
                            >
                              {st === 'all' ? <Check className="h-2.5 w-2.5" strokeWidth={3} />
                                : st === 'some' ? <Minus className="h-2.5 w-2.5" strokeWidth={3} /> : null}
                            </button>
                          )
                        })()}
                        {!licensed && <Lock className="h-3 w-3 shrink-0" aria-hidden />}
                        {m.label}
                        {!licensed && <span className="sr-only">(module not licensed for this brand)</span>}
                      </span>
                    </th>
                    {data.actions.map((a) => {
                      const key = `${m.key}:${a}`
                      const on = draft.has(key)
                      const locked = !licensed
                      const grants = data.cells?.[key] ?? []
                      const title = locked
                        ? 'Not licensed for this brand — enable it under Licensing'
                        : grants.length > 0
                          ? `Grants: ${grants.join(', ')}`
                          : undefined
                      return (
                        <td key={a} className="px-3 py-2.5 text-center">
                          <button
                            type="button"
                            onClick={() => !locked && toggle(key)}
                            aria-pressed={on}
                            aria-label={`${m.label} – ${a}${locked ? ' (not licensed)' : ''}`}
                            disabled={!canEdit || locked}
                            title={title}
                            className={cn(
                              'inline-flex h-6 w-6 items-center justify-center rounded-md border transition-colors',
                              on
                                ? locked
                                  ? 'border-gray-300 bg-gray-300 text-white'
                                  : 'border-lg-green bg-lg-green text-white'
                                : 'border-gray-200 bg-gray-50 hover:border-gray-300',
                              (!canEdit || locked) && 'cursor-not-allowed opacity-60 hover:border-gray-200',
                            )}
                          >
                            {on && <Check className="h-3.5 w-3.5" strokeWidth={3} />}
                          </button>
                        </td>
                      )
                    })}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      <RoleFormModal
        open={roleModal !== null}
        mode={roleModal?.mode ?? 'create'}
        source={roleModal?.source ?? null}
        onClose={() => setRoleModal(null)}
        onSaved={onRoleSaved}
      />
    </div>
  )
}
