import { useState } from 'react'
import { Loader2 } from 'lucide-react'
import {
  useMobileAppConfigsInfinite,
  useCreateMobileAppConfig,
  useUpdateMobileAppConfig,
  useDeleteMobileAppConfig,
} from '@/hooks/useCms'
import { useInfiniteScroll } from '@/hooks/useInfiniteScroll'
import { usePermissions } from '@/hooks/usePermissions'
import { percentageInt } from '@/lib/validation'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type {
  MobileAppConfigDto,
  CreateMobileAppConfigRequest,
  UpdateMobileAppConfigRequest,
} from '@/types/api'
import { formatDate } from '@/lib/utils'

const PLATFORMS = ['android', 'ios', 'web']
const STATUSES = ['active', 'inactive', 'archived']

// ── Delete Confirm ────────────────────────────────────────────────────────────

interface DeleteConfirmProps {
  item: MobileAppConfigDto
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}

function DeleteConfirm({ item, onConfirm, onCancel, isPending }: DeleteConfirmProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
        <h3 className="text-base font-semibold text-gray-900 mb-2">Archive config?</h3>
        <p className="text-sm text-gray-500 mb-6">
          Config key <span className="font-mono font-medium text-gray-700">{item.configKey}</span> will be archived.
        </p>
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={onCancel} disabled={isPending}>
            Cancel
          </Button>
          <Button variant="destructive" size="sm" onClick={onConfirm} disabled={isPending}>
            {isPending ? 'Archiving…' : 'Archive'}
          </Button>
        </div>
      </div>
    </div>
  )
}

// ── Form Modal ────────────────────────────────────────────────────────────────

type FormFields = {
  appType: string
  platform: string
  configKey: string
  configValue: string
  description: string
  isForceUpdate: boolean
  minAppVersion: string
  maxAppVersion: string
  rolloutPercent: string
  isActive: boolean
  status: string
}

function defaultFields(c?: MobileAppConfigDto | null): FormFields {
  return {
    appType: c?.appType ?? 'customer',
    platform: c?.platform ?? 'android',
    configKey: c?.configKey ?? '',
    configValue: c?.configValue ?? '',
    description: c?.description ?? '',
    isForceUpdate: c?.isForceUpdate ?? false,
    minAppVersion: c?.minAppVersion ?? '',
    maxAppVersion: c?.maxAppVersion ?? '',
    rolloutPercent: c?.rolloutPercent != null ? String(c.rolloutPercent) : '',
    isActive: c?.isActive ?? true,
    status: c?.status ?? 'active',
  }
}

function FormField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <Label className="text-xs text-gray-600">{label}</Label>
      {children}
    </div>
  )
}

interface FormModalProps {
  initial?: MobileAppConfigDto | null
  onClose: () => void
}

function FormModal({ initial, onClose }: FormModalProps) {
  const isEdit = Boolean(initial)
  const [fields, setFields] = useState<FormFields>(() => defaultFields(initial))
  const [error, setError] = useState<string | null>(null)

  const createMutation = useCreateMobileAppConfig()
  const updateMutation = useUpdateMobileAppConfig(initial?.id ?? '')
  const isPending = createMutation.isPending || updateMutation.isPending

  function set(key: keyof FormFields, value: string | boolean) {
    setFields((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)

    let rollout: number | null = null
    if (fields.rolloutPercent.trim() !== '') {
      const parsed = percentageInt.safeParse(Number(fields.rolloutPercent))
      if (!parsed.success) {
        setError('Rollout % must be a whole number between 0 and 100.')
        return
      }
      rollout = parsed.data
    }

    const base = {
      appType: fields.appType,
      platform: fields.platform,
      configKey: fields.configKey,
      configValue: fields.configValue,
      description: fields.description || null,
      isForceUpdate: fields.isForceUpdate,
      minAppVersion: fields.minAppVersion || null,
      maxAppVersion: fields.maxAppVersion || null,
      rolloutPercent: rollout,
      isActive: fields.isActive,
    }

    try {
      if (isEdit && initial) {
        const payload: UpdateMobileAppConfigRequest = { ...base, status: fields.status }
        await updateMutation.mutateAsync(payload)
      } else {
        const payload: CreateMobileAppConfigRequest = base
        await createMutation.mutateAsync(payload)
      }
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 overflow-y-auto py-8">
      <div className="bg-white rounded-lg shadow-xl w-full max-w-xl mx-4">
        <div className="px-6 py-4 border-b border-gray-200">
          <h2 className="text-base font-semibold text-gray-900">
            {isEdit ? 'Edit App Config' : 'New App Config'}
          </h2>
        </div>
        <form onSubmit={(e) => void handleSubmit(e)}>
          <div className="px-6 py-4 space-y-4 max-h-[65vh] overflow-y-auto">
            <div className="grid grid-cols-2 gap-4">
              <FormField label="App Type *">
                <Input
                  value={fields.appType}
                  onChange={(e) => set('appType', e.target.value)}
                  placeholder="customer"
                  required
                />
              </FormField>
              <FormField label="Platform *">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.platform}
                  onChange={(e) => set('platform', e.target.value)}
                  required
                >
                  {PLATFORMS.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </FormField>
            </div>

            <FormField label="Config Key *">
              <Input
                value={fields.configKey}
                onChange={(e) => set('configKey', e.target.value)}
                placeholder="FORCE_UPDATE_VERSION"
                required
              />
            </FormField>

            <FormField label="Config Value *">
              <Input
                value={fields.configValue}
                onChange={(e) => set('configValue', e.target.value)}
                placeholder="2.5.0"
                required
              />
            </FormField>

            <FormField label="Description">
              <Input
                value={fields.description}
                onChange={(e) => set('description', e.target.value)}
              />
            </FormField>

            <div className="grid grid-cols-3 gap-4">
              <FormField label="Min App Version">
                <Input
                  value={fields.minAppVersion}
                  onChange={(e) => set('minAppVersion', e.target.value)}
                  placeholder="1.0.0"
                />
              </FormField>
              <FormField label="Max App Version">
                <Input
                  value={fields.maxAppVersion}
                  onChange={(e) => set('maxAppVersion', e.target.value)}
                  placeholder="2.9.9"
                />
              </FormField>
              <FormField label="Rollout % (0-100)">
                <Input
                  type="number"
                  value={fields.rolloutPercent}
                  onChange={(e) => set('rolloutPercent', e.target.value)}
                  min={0}
                  max={100}
                />
              </FormField>
            </div>

            {isEdit && (
              <FormField label="Status">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.status}
                  onChange={(e) => set('status', e.target.value)}
                >
                  {STATUSES.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </FormField>
            )}

            <div className="flex gap-6">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={fields.isForceUpdate}
                  onChange={(e) => set('isForceUpdate', e.target.checked)}
                  className="rounded"
                />
                Force Update
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={fields.isActive}
                  onChange={(e) => set('isActive', e.target.checked)}
                  className="rounded"
                />
                Active
              </label>
            </div>

            {error && (
              <p className="text-sm text-red-600 rounded bg-red-50 px-3 py-2">{error}</p>
            )}
          </div>

          <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
            <Button type="button" variant="outline" size="sm" onClick={onClose} disabled={isPending}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={isPending}>
              {isPending ? 'Saving…' : isEdit ? 'Save changes' : 'Create'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── Tab ───────────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' ? 'success' : status === 'archived' ? 'destructive' : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

export function MobileAppConfigTab() {
  // Backend gate: every config mutation requires permission:cms.appconfig.manage.
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('cms.appconfig.manage')
  const [showForm, setShowForm] = useState(false)
  const [editTarget, setEditTarget] = useState<MobileAppConfigDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<MobileAppConfigDto | null>(null)

  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage,
  } = useMobileAppConfigsInfinite()
  const deleteMutation = useDeleteMobileAppConfig()

  const sentinelRef = useInfiniteScroll({ hasNextPage, isFetchingNextPage, fetchNextPage })

  function handleEdit(row: MobileAppConfigDto) {
    setEditTarget(row)
    setShowForm(true)
  }

  function handleCloseForm() {
    setShowForm(false)
    setEditTarget(null)
  }

  async function handleDelete() {
    if (!deleteTarget) return
    await deleteMutation.mutateAsync(deleteTarget.id)
    setDeleteTarget(null)
  }

  const columns: Column<MobileAppConfigDto>[] = [
    {
      header: 'Platform',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.platform}
        </Badge>
      ),
    },
    { header: 'App Type', accessor: 'appType', className: 'text-xs' },
    { header: 'Key', accessor: 'configKey', className: 'font-mono text-xs' },
    { header: 'Value', accessor: 'configValue', className: 'text-xs max-w-xs truncate' },
    {
      header: 'Force Update',
      accessor: (r) =>
        r.isForceUpdate ? (
          <Badge variant="warning">Yes</Badge>
        ) : (
          <span className="text-gray-300 text-xs">No</span>
        ),
    },
    {
      header: 'Rollout',
      accessor: (r) =>
        r.rolloutPercent != null ? (
          <span className="tabular-nums text-xs">{r.rolloutPercent}%</span>
        ) : (
          <span className="text-gray-300 text-xs">—</span>
        ),
    },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
    ...(canManage
      ? [{
          header: '',
          accessor: (r: MobileAppConfigDto) => (
            <div className="flex gap-1 justify-end">
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  handleEdit(r)
                }}
              >
                Edit
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={(e) => {
                  e.stopPropagation()
                  setDeleteTarget(r)
                }}
                className="text-red-600 hover:text-red-700 hover:bg-red-50"
              >
                Archive
              </Button>
            </div>
          ),
          className: 'w-36',
        } satisfies Column<MobileAppConfigDto>]
      : []),
  ]

  if (isLoading) return <LoadingState message="Loading app config..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  const items = data?.pages.flatMap((p) => p.list) ?? []
  const total = data?.pages[0]?.totalCount

  return (
    <div>
      <div className="flex items-center justify-between px-4 pt-3 pb-2">
        {total !== undefined && (
          <p className="text-sm text-gray-500">{total} config{total === 1 ? '' : 's'}</p>
        )}
        {canManage && (
          <Button size="sm" onClick={() => setShowForm(true)} className="ml-auto">
            + New Config
          </Button>
        )}
      </div>

      <DataTable
        columns={columns}
        data={items}
        keyFn={(r) => r.id}
        emptyMessage="No mobile app configs found."
      />
      <div ref={sentinelRef} className="h-1" />
      {isFetchingNextPage && (
        <div className="flex items-center justify-center py-4 text-gray-400">
          <Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading more…
        </div>
      )}

      {showForm && <FormModal initial={editTarget} onClose={handleCloseForm} />}

      {deleteTarget && (
        <DeleteConfirm
          item={deleteTarget}
          onConfirm={() => void handleDelete()}
          onCancel={() => setDeleteTarget(null)}
          isPending={deleteMutation.isPending}
        />
      )}
    </div>
  )
}
