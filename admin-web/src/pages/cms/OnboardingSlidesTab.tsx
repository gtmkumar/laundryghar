import { useState } from 'react'
import {
  useOnboardingSlides,
  useCreateOnboardingSlide,
  useUpdateOnboardingSlide,
  useDeleteOnboardingSlide,
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
  OnboardingSlideDto,
  CreateOnboardingSlideRequest,
  UpdateOnboardingSlideRequest,
} from '@/types/api'
import { formatDate } from '@/lib/utils'

const APP_TYPES = ['customer', 'rider', 'staff', 'pos']
const STATUSES = ['active', 'inactive', 'archived']

// ── Delete Confirm ────────────────────────────────────────────────────────────

interface DeleteConfirmProps {
  item: OnboardingSlideDto
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}

function DeleteConfirm({ item, onConfirm, onCancel, isPending }: DeleteConfirmProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
        <h3 className="text-base font-semibold text-gray-900 mb-2">Archive slide?</h3>
        <p className="text-sm text-gray-500 mb-6">
          <span className="font-medium text-gray-700">{item.title}</span> will be archived.
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
  title: string
  titleLocalized: string
  description: string
  descriptionLocalized: string
  imageUrl: string
  imageDarkUrl: string
  ctaText: string
  ctaDeeplink: string
  backgroundColor: string
  textColor: string
  displayOrder: string
  isActive: boolean
  status: string
}

function defaultFields(s?: OnboardingSlideDto | null): FormFields {
  return {
    appType: s?.appType ?? 'customer',
    title: s?.title ?? '',
    titleLocalized: s?.titleLocalized ?? '',
    description: s?.description ?? '',
    descriptionLocalized: s?.descriptionLocalized ?? '',
    imageUrl: s?.imageUrl ?? '',
    imageDarkUrl: s?.imageDarkUrl ?? '',
    ctaText: s?.ctaText ?? '',
    ctaDeeplink: s?.ctaDeeplink ?? '',
    backgroundColor: s?.backgroundColor ?? '',
    textColor: s?.textColor ?? '',
    displayOrder: String(s?.displayOrder ?? 0),
    isActive: s?.isActive ?? true,
    status: s?.status ?? 'active',
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
  initial?: OnboardingSlideDto | null
  onClose: () => void
}

function FormModal({ initial, onClose }: FormModalProps) {
  const isEdit = Boolean(initial)
  const [fields, setFields] = useState<FormFields>(() => defaultFields(initial))
  const [error, setError] = useState<string | null>(null)

  const createMutation = useCreateOnboardingSlide()
  const updateMutation = useUpdateOnboardingSlide(initial?.id ?? '')
  const isPending = createMutation.isPending || updateMutation.isPending

  function set(key: keyof FormFields, value: string | boolean) {
    setFields((f) => ({ ...f, [key]: value }))
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)

    try {
      if (isEdit && initial) {
        const payload: UpdateOnboardingSlideRequest = {
          appType: fields.appType,
          title: fields.title,
          titleLocalized: fields.titleLocalized,
          description: fields.description || null,
          descriptionLocalized: fields.descriptionLocalized,
          imageUrl: fields.imageUrl,
          imageDarkUrl: fields.imageDarkUrl || null,
          ctaText: fields.ctaText || null,
          ctaDeeplink: fields.ctaDeeplink || null,
          backgroundColor: fields.backgroundColor || null,
          textColor: fields.textColor || null,
          displayOrder: parseInt(fields.displayOrder, 10) || 0,
          isActive: fields.isActive,
          status: fields.status,
        }
        await updateMutation.mutateAsync(payload)
      } else {
        const payload: CreateOnboardingSlideRequest = {
          appType: fields.appType,
          title: fields.title,
          titleLocalized: fields.titleLocalized,
          description: fields.description || null,
          descriptionLocalized: fields.descriptionLocalized,
          imageUrl: fields.imageUrl,
          imageDarkUrl: fields.imageDarkUrl || null,
          ctaText: fields.ctaText || null,
          ctaDeeplink: fields.ctaDeeplink || null,
          backgroundColor: fields.backgroundColor || null,
          textColor: fields.textColor || null,
          displayOrder: parseInt(fields.displayOrder, 10) || 0,
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
            {isEdit ? 'Edit Onboarding Slide' : 'New Onboarding Slide'}
          </h2>
        </div>
        <form onSubmit={(e) => void handleSubmit(e)}>
          <div className="px-6 py-4 space-y-4 max-h-[65vh] overflow-y-auto">
            <div className="grid grid-cols-2 gap-4">
              <FormField label="App Type *">
                <select
                  className="flex h-9 w-full rounded-md border border-gray-300 bg-white px-3 py-1 text-sm"
                  value={fields.appType}
                  onChange={(e) => set('appType', e.target.value)}
                  required
                >
                  {APP_TYPES.map((t) => (
                    <option key={t} value={t}>
                      {t}
                    </option>
                  ))}
                </select>
              </FormField>
              <FormField label="Display Order">
                <Input
                  type="number"
                  value={fields.displayOrder}
                  onChange={(e) => set('displayOrder', e.target.value)}
                  min={0}
                />
              </FormField>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="Title *">
                <Input
                  value={fields.title}
                  onChange={(e) => set('title', e.target.value)}
                  required
                />
              </FormField>
              <FormField label="Title (Localized) *">
                <Input
                  value={fields.titleLocalized}
                  onChange={(e) => set('titleLocalized', e.target.value)}
                  required
                />
              </FormField>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="Description">
                <Input
                  value={fields.description}
                  onChange={(e) => set('description', e.target.value)}
                />
              </FormField>
              <FormField label="Description (Localized) *">
                <Input
                  value={fields.descriptionLocalized}
                  onChange={(e) => set('descriptionLocalized', e.target.value)}
                  required
                />
              </FormField>
            </div>

            <FormField label="Image URL *">
              <Input
                value={fields.imageUrl}
                onChange={(e) => set('imageUrl', e.target.value)}
                placeholder="https://cdn.example.com/slide1.png"
                required
              />
            </FormField>

            <FormField label="Image URL (Dark mode)">
              <Input
                value={fields.imageDarkUrl}
                onChange={(e) => set('imageDarkUrl', e.target.value)}
              />
            </FormField>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="CTA Text">
                <Input
                  value={fields.ctaText}
                  onChange={(e) => set('ctaText', e.target.value)}
                  placeholder="Get Started"
                />
              </FormField>
              <FormField label="CTA Deeplink">
                <Input
                  value={fields.ctaDeeplink}
                  onChange={(e) => set('ctaDeeplink', e.target.value)}
                  placeholder="app://home"
                />
              </FormField>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <FormField label="Background Color">
                <Input
                  value={fields.backgroundColor}
                  onChange={(e) => set('backgroundColor', e.target.value)}
                  placeholder="#FFFFFF"
                />
              </FormField>
              <FormField label="Text Color">
                <Input
                  value={fields.textColor}
                  onChange={(e) => set('textColor', e.target.value)}
                  placeholder="#000000"
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

            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={fields.isActive}
                onChange={(e) => set('isActive', e.target.checked)}
                className="rounded"
              />
              Active
            </label>

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

export function OnboardingSlidesTab() {
  const [page, setPage] = useState(1)
  const [showForm, setShowForm] = useState(false)
  const [editTarget, setEditTarget] = useState<OnboardingSlideDto | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<OnboardingSlideDto | null>(null)

  const { data, isLoading, isError, error, refetch } = useOnboardingSlides({
    page,
    pageSize: 20,
  })
  const deleteMutation = useDeleteOnboardingSlide()

  function handleEdit(row: OnboardingSlideDto) {
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

  const columns: Column<OnboardingSlideDto>[] = [
    {
      header: 'App Type',
      accessor: (r) => (
        <Badge variant="secondary" className="capitalize">
          {r.appType}
        </Badge>
      ),
    },
    { header: 'Title', accessor: 'title' },
    { header: 'Order', accessor: (r) => String(r.displayOrder), className: 'w-16 tabular-nums' },
    {
      header: 'Active',
      accessor: (r) =>
        r.isActive ? (
          <Badge variant="success">Yes</Badge>
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

  if (isLoading) return <LoadingState message="Loading onboarding slides..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <div>
      <div className="flex justify-end px-4 pt-3 pb-2">
        <Button size="sm" onClick={() => setShowForm(true)}>
          + New Slide
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={data?.list ?? []}
        keyFn={(r) => r.id}
        emptyMessage="No onboarding slides found."
      />
      <Pagination
        page={page}
        hasPrevious={data?.hasPreviousPage ?? false}
        hasNext={data?.hasNextPage ?? false}
        onPrevious={() => setPage((p) => Math.max(1, p - 1))}
        onNext={() => setPage((p) => p + 1)}
      />

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
