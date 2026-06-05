import { useState } from 'react'
import {
  useNotificationTemplates,
  useCreateNotificationTemplate,
  useUpdateNotificationTemplate,
  useDeleteNotificationTemplate,
} from '@/hooks/useCms'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Pagination } from '@/components/shared/Pagination'
import { DataTable, type Column } from '@/components/shared/DataTable'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type {
  NotificationTemplateDto,
  CreateNotificationTemplateRequest,
  UpdateNotificationTemplateRequest,
} from '@/types/api'
import { formatDate } from '@/lib/utils'

// ── Constants ─────────────────────────────────────────────────────────────────

const CHANNELS = ['sms', 'whatsapp', 'email', 'push', 'in_app', 'voice']
const STATUSES = ['active', 'inactive', 'archived']

// ── Delete Confirm ─────────────────────────────────────────────────────────────

interface DeleteConfirmProps {
  item: NotificationTemplateDto
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}

function DeleteConfirm({ item, onConfirm, onCancel, isPending }: DeleteConfirmProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
        <h3 className="text-base font-semibold text-gray-900 mb-2">Archive template?</h3>
        <p className="text-sm text-gray-500 mb-6">
          <span className="font-medium text-gray-700">{item.name}</span> will be archived
          and set inactive. This can be reversed via the Edit form.
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

interface FormModalProps {
  initial?: NotificationTemplateDto | null
  onClose: () => void
}

type FormFields = {
  code: string
  name: string
  description: string
  channel: string
  category: string
  locale: string
  subjectTemplate: string
  bodyTemplate: string
  variables: string
  versionNumber: string
  isTransactional: boolean
  isActive: boolean
  status: string
}

function defaultFields(t?: NotificationTemplateDto | null): FormFields {
  return {
    code: t?.code ?? '',
    name: t?.name ?? '',
    description: t?.description ?? '',
    channel: t?.channel ?? 'sms',
    category: t?.category ?? '',
    locale: t?.locale ?? 'en',
    subjectTemplate: t?.subjectTemplate ?? '',
    bodyTemplate: t?.bodyTemplate ?? '',
    variables: t?.variables ?? '{}',
    versionNumber: String(t?.versionNumber ?? 1),
    isTransactional: t?.isTransactional ?? false,
    isActive: t?.isActive ?? true,
    status: t?.status ?? 'active',
  }
}

function FormModal({ initial, onClose }: FormModalProps) {
  const isEdit = Boolean(initial)
  const [fields, setFields] = useState<FormFields>(() => defaultFields(initial))
  const [error, setError] = useState<string | null>(null)

  const createMutation = useCreateNotificationTemplate()
  const updateMutation = useUpdateNotificationTemplate(initial?.id ?? '')

  const isPending = createMutation.isPending || updateMutation.isPending

  function set(key: keyof FormFields, value: string | boolean) {
    setFields((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)

    try {
      if (isEdit && initial) {
        const payload: UpdateNotificationTemplateRequest = {
          name: fields.name,
          description: fields.description || null,
          subjectTemplate: fields.subjectTemplate || null,
          bodyTemplate: fields.bodyTemplate,
          variables: fields.variables,
          isTransactional: fields.isTransactional,
          isActive: fields.isActive,
          status: fields.status,
        }
        await updateMutation.mutateAsync(payload)
      } else {
        const payload: CreateNotificationTemplateRequest = {
          code: fields.code,
          name: fields.name,
          description: fields.description || null,
          channel: fields.channel,
          category: fields.category,
          locale: fields.locale,
          subjectTemplate: fields.subjectTemplate || null,
          bodyTemplate: fields.bodyTemplate,
          variables: fields.variables,
          versionNumber: parseInt(fields.versionNumber, 10) || 1,
          isTransactional: fields.isTransactional,
          isActive: fields.isActive,
        }
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
            {isEdit ? 'Edit Template' : 'New Notification Template'}
          </h2>
        </div>
        <form onSubmit={(e) => void handleSubmit(e)}>
          <div className="px-6 py-4 space-y-4 max-h-[65vh] overflow-y-auto">
            {!isEdit && (
              <div className="grid grid-cols-2 gap-4">
                <FormField label="Code *">
                  <Input
                    value={fields.code}
                    onChange={(e) => set('code', e.target.value)}
                    placeholder="ORDER_CONFIRMED"
                    required
                  />
                </FormField>
                <FormField label="Channel *">
                  <select
                    className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                    value={fields.channel}
                    onChange={(e) => set('channel', e.target.value)}
                    required
                  >
                    {CHANNELS.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                </FormField>
              </div>
            )}

            <FormField label="Name *">
              <Input
                value={fields.name}
                onChange={(e) => set('name', e.target.value)}
                required
              />
            </FormField>

            {!isEdit && (
              <div className="grid grid-cols-2 gap-4">
                <FormField label="Category *">
                  <Input
                    value={fields.category}
                    onChange={(e) => set('category', e.target.value)}
                    placeholder="order"
                    required
                  />
                </FormField>
                <FormField label="Locale *">
                  <Input
                    value={fields.locale}
                    onChange={(e) => set('locale', e.target.value)}
                    placeholder="en"
                    required
                  />
                </FormField>
              </div>
            )}

            <FormField label="Description">
              <Input
                value={fields.description}
                onChange={(e) => set('description', e.target.value)}
              />
            </FormField>

            <FormField label="Subject Template">
              <Input
                value={fields.subjectTemplate}
                onChange={(e) => set('subjectTemplate', e.target.value)}
                placeholder="Your order {{order_number}} is confirmed"
              />
            </FormField>

            <FormField label="Body Template *">
              <textarea
                className="flex w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm min-h-[80px] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500"
                value={fields.bodyTemplate}
                onChange={(e) => set('bodyTemplate', e.target.value)}
                required
              />
            </FormField>

            <FormField label="Variables (JSON)">
              <Input
                value={fields.variables}
                onChange={(e) => set('variables', e.target.value)}
                placeholder='{"order_number":"string"}'
              />
            </FormField>

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
                  checked={fields.isTransactional}
                  onChange={(e) => set('isTransactional', e.target.checked)}
                  className="rounded"
                />
                Transactional
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

function FormField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <Label className="text-xs text-gray-600">{label}</Label>
      {children}
    </div>
  )
}

// ── Table ─────────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const variant =
    status === 'active' ? 'success' : status === 'archived' ? 'destructive' : 'secondary'
  return (
    <Badge variant={variant} className="capitalize">
      {status}
    </Badge>
  )
}

// ── Tab ───────────────────────────────────────────────────────────────────────

export function NotificationTemplatesTab() {
  const [page, setPage] = useState(1)
  const [showForm, setShowForm] = useState(false)
  const [editTarget, setEditTarget] = useState<NotificationTemplateDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<NotificationTemplateDto | null>(null)

  const { data, isLoading, isError, error, refetch } = useNotificationTemplates({
    page,
    pageSize: 20,
  })
  const deleteMutation = useDeleteNotificationTemplate()

  function handleEdit(row: NotificationTemplateDto) {
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

  const columns: Column<NotificationTemplateDto>[] = [
    { header: 'Code', accessor: 'code', className: 'font-mono text-xs w-40' },
    { header: 'Name', accessor: 'name' },
    {
      header: 'Channel',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.channel}
        </Badge>
      ),
    },
    { header: 'Category', accessor: 'category', className: 'text-xs text-gray-500' },
    { header: 'Locale', accessor: 'locale', className: 'w-16 text-xs' },
    {
      header: 'Transactional',
      accessor: (r) =>
        r.isTransactional ? (
          <Badge variant="default">Yes</Badge>
        ) : (
          <span className="text-gray-300 text-xs">No</span>
        ),
    },
    { header: 'Status', accessor: (r) => <StatusBadge status={r.status} /> },
    { header: 'Updated', accessor: (r) => formatDate(r.updatedAt) },
    {
      header: '',
      accessor: (r) => (
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
    },
  ]

  if (isLoading) return <LoadingState message="Loading notification templates..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <div>
      <div className="flex justify-end px-4 pt-3 pb-2">
        <Button size="sm" onClick={() => setShowForm(true)}>
          + New Template
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={data?.list ?? []}
        keyFn={(r) => r.id}
        emptyMessage="No notification templates found."
      />
      <Pagination
        page={page}
        hasPrevious={data?.hasPreviousPage ?? false}
        hasNext={data?.hasNextPage ?? false}
        onPrevious={() => setPage((p) => Math.max(1, p - 1))}
        onNext={() => setPage((p) => p + 1)}
      />

      {showForm && (
        <FormModal initial={editTarget} onClose={handleCloseForm} />
      )}

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
