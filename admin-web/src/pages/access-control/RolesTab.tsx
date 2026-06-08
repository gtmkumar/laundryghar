import { useEffect, useMemo, useState } from 'react'
import { Loader2, Check, Copy, Plus } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useSetRoleCell } from '@/hooks/useAccessControl'
import type { AccessRoles, AccessRoleSummary } from '@/types/api'

interface Props {
  query: { data?: AccessRoles; isLoading: boolean; isError: boolean }
  search?: string
}

const SCOPE_LABEL: Record<string, string> = {
  platform: 'Platform-wide',
  brand: 'Enterprise-wide',
  franchise: 'Franchise-scoped',
  store: 'Store-scoped',
  warehouse: 'Warehouse-scoped',
}

export function RolesTab({ query, search }: Props) {
  const { data, isLoading, isError } = query
  const setCell = useSetRoleCell()

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
  useEffect(() => {
    if (selected) setDraft(new Set(selected.onCells))
  }, [selected?.id, selected?.onCells])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading roles…
      </div>
    )
  }
  if (isError || !data) {
    return <div className="py-24 text-center text-sm text-red-600">Couldn’t load roles.</div>
  }
  if (!selected) {
    return <div className="py-24 text-center text-sm text-gray-400">No roles match “{search}”.</div>
  }

  const original = new Set(selected.onCells)
  const dirty =
    draft.size !== original.size || [...draft].some((c) => !original.has(c))

  const toggle = (cellKey: string) => {
    setDraft((prev) => {
      const next = new Set(prev)
      if (next.has(cellKey)) next.delete(cellKey)
      else next.add(cellKey)
      return next
    })
  }

  const save = async () => {
    if (!selected) return
    setSaving(true)
    try {
      const changes: { cellKey: string; enabled: boolean }[] = []
      for (const m of data.modules)
        for (const a of data.actions) {
          const key = `${m.key}:${a}`
          const was = original.has(key)
          const now = draft.has(key)
          if (was !== now) changes.push({ cellKey: key, enabled: now })
        }
      for (const ch of changes) {
        await setCell.mutateAsync({ roleId: selected.id, cellKey: ch.cellKey, enabled: ch.enabled })
      }
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
                    onClick={() => setSelectedId(r.id)}
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
        <button
          type="button"
          className="mt-1 flex w-full items-center justify-center gap-1.5 rounded-xl border border-dashed border-gray-300 py-2 text-xs font-medium text-gray-400 hover:border-gray-400 hover:text-gray-600"
        >
          <Plus className="h-3.5 w-3.5" /> New custom role
        </button>
      </div>

      {/* Right: permission matrix */}
      <div className="rounded-2xl border border-gray-200 bg-white">
        <div className="flex flex-wrap items-center gap-3 border-b border-gray-100 px-5 py-4">
          <div>
            <div className="flex items-center gap-2">
              <h3 className="text-lg font-bold text-gray-900">{selected.name}</h3>
              <span className="rounded-full bg-lg-green/10 px-2 py-0.5 text-xs font-medium text-lg-green">
                {SCOPE_LABEL[selected.scopeType] ?? selected.scopeType}
              </span>
            </div>
            {selected.description && <p className="mt-0.5 text-sm text-gray-400">{selected.description}</p>}
          </div>
          <div className="ml-auto flex items-center gap-2">
            <button type="button" className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50">
              <Copy className="h-3.5 w-3.5" /> Clone
            </button>
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
          </div>
        </div>

        {/* Matrix */}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-gray-400">
                <th className="px-5 py-3">Module</th>
                {data.actions.map((a) => (
                  <th key={a} className="px-3 py-3 text-center">{a}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.modules.map((m) => (
                <tr key={m.key} className="border-t border-gray-50">
                  <td className="px-5 py-2.5 font-medium text-gray-700">{m.label}</td>
                  {data.actions.map((a) => {
                    const key = `${m.key}:${a}`
                    const on = draft.has(key)
                    return (
                      <td key={a} className="px-3 py-2.5 text-center">
                        <button
                          type="button"
                          onClick={() => toggle(key)}
                          aria-pressed={on}
                          className={cn(
                            'inline-flex h-6 w-6 items-center justify-center rounded-md border transition-colors',
                            on
                              ? 'border-lg-green bg-lg-green text-white'
                              : 'border-gray-200 bg-gray-50 text-transparent hover:border-gray-300',
                          )}
                        >
                          <Check className="h-3.5 w-3.5" strokeWidth={3} />
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
