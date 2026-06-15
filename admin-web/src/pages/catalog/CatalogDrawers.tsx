import { useEffect, useMemo, useRef, useState } from 'react'
import { ImagePlus, Layers, Plus, Save, Shirt, Trash2, Wrench } from 'lucide-react'
import {
  useCreateServiceCategory,
  useUpdateServiceCategory,
  useDeleteServiceCategory,
  useCreateService,
  useUpdateService,
  useDeleteService,
  useCreateItem,
  useUpdateItem,
  useDeleteItem,
  useUploadItemImage,
  useDeleteItemImage,
  useItemImageUrl,
  useServiceCategoriesInfinite,
  useItemGroups,
} from '@/hooks/useCatalog'
import {
  FormDrawer,
  DrawerSection,
  Field,
  drawerInputCls,
} from '@/components/shared/FormDrawer'
import { apiErrorMessage } from '@/lib/apiError'
import type {
  ServiceCategoryDto,
  ServiceDto,
  ItemDto,
} from '@/types/api'
import { buildNameLocalized, parseNameLocalized } from './localized'

const STATUSES = [
  { value: 'active', label: 'Active' },
  { value: 'inactive', label: 'Inactive' },
  { value: 'archived', label: 'Archived' },
] as const

/**
 * Drives how the POS prices a service. Values MUST match the DB check constraint
 * `services_pricing_model_check` (per_item / per_kg / per_sqft / per_pair /
 * per_side / flat) — anything else 400s with a 23514 constraint violation.
 */
// Small constant co-located with this drawer's <select>; not worth a separate module.
// eslint-disable-next-line react-refresh/only-export-components
export const PRICING_MODELS = [
  { value: 'per_item', label: 'Per item' },
  { value: 'per_kg', label: 'Per kg' },
  { value: 'per_sqft', label: 'Per sq ft' },
  { value: 'per_pair', label: 'Per pair' },
  { value: 'per_side', label: 'Per side' },
  { value: 'flat', label: 'Flat' },
] as const

/** Two-input English + Hindi localized-name editor (serialized to jsonb). */
function LocalizedNameFields({
  en,
  hi,
  onEn,
  onHi,
  placeholder,
}: {
  en: string
  hi: string
  onEn: (v: string) => void
  onHi: (v: string) => void
  placeholder?: string
}) {
  return (
    <div className="grid grid-cols-2 gap-3">
      <Field label="Localized name (EN) *">
        <input
          value={en}
          onChange={(e) => onEn(e.target.value)}
          className={drawerInputCls}
          placeholder={placeholder ?? 'Dry Clean'}
        />
      </Field>
      <Field label="Localized name (HI)">
        <input
          value={hi}
          onChange={(e) => onHi(e.target.value)}
          className={drawerInputCls}
          placeholder="ड्राई क्लीन"
        />
      </Field>
    </div>
  )
}

const checkboxCls = 'h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30'

// ══════════════════════════════════════════════════════════════════════════════
// Service Category
// ══════════════════════════════════════════════════════════════════════════════

interface CategoryDrawerProps {
  open: boolean
  category?: ServiceCategoryDto | null
  onClose: () => void
}

export function CategoryEditDrawer({ open, category, onClose }: CategoryDrawerProps) {
  const isEdit = !!category
  const create = useCreateServiceCategory()
  const update = useUpdateServiceCategory()

  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [nameEn, setNameEn] = useState('')
  const [nameHi, setNameHi] = useState('')
  const [description, setDescription] = useState('')
  const [colorHex, setColorHex] = useState('')
  const [displayOrder, setDisplayOrder] = useState('0')
  const [isVisibleMobile, setVisibleMobile] = useState(true)
  const [isVisiblePos, setVisiblePos] = useState(true)
  const [status, setStatus] = useState('active')
  const [error, setError] = useState<string | null>(null)

  // Seed the form when opened or the target category changes
  // (adjust-state-while-rendering, not an effect).
  const [seededFor, setSeededFor] = useState<{ open: boolean; category: ServiceCategoryDto | null | undefined }>({ open, category })
  if ((seededFor.open !== open || seededFor.category !== category) && open) {
    setSeededFor({ open, category })
    setError(null)
    if (category) {
      const loc = parseNameLocalized(category.nameLocalized)
      setCode(category.code)
      setName(category.name)
      setNameEn(loc.en || category.name)
      setNameHi(loc.hi)
      setDescription(category.description ?? '')
      setColorHex(category.colorHex ?? '')
      setDisplayOrder(String(category.displayOrder))
      setVisibleMobile(category.isVisibleMobile)
      setVisiblePos(category.isVisiblePos)
      setStatus(category.status)
    } else {
      setCode('')
      setName('')
      setNameEn('')
      setNameHi('')
      setDescription('')
      setColorHex('')
      setDisplayOrder('0')
      setVisibleMobile(true)
      setVisiblePos(true)
      setStatus('active')
    }
  } else if (seededFor.open !== open) {
    // Keep the tracker in sync when closing so the next open re-seeds.
    setSeededFor({ open, category })
  }

  if (!open) return null

  const submit = async () => {
    setError(null)
    if (!isEdit && !code.trim()) return setError('Code is required.')
    if (!name.trim()) return setError('Name is required.')
    if (!nameEn.trim()) return setError('Localized name (EN) is required.')

    const common = {
      name: name.trim(),
      nameLocalized: buildNameLocalized(nameEn, nameHi),
      description: description.trim() || null,
      iconUrl: null,
      imageUrl: null,
      colorHex: colorHex.trim() || null,
      displayOrder: Number(displayOrder) || 0,
      isVisibleMobile,
      isVisiblePos,
    }
    try {
      if (isEdit && category) {
        await update.mutateAsync({ id: category.id, payload: { ...common, status } })
      } else {
        await create.mutateAsync({ ...common, code: code.trim(), requiresWarehouseCap: [] })
      }
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not save the category.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Layers}
      eyebrow="Catalog · Service category"
      title={isEdit ? `Edit ${category!.name}` : 'New service category'}
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save category' : 'Create category'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={create.isPending || update.isPending}
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed.' : undefined}>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              disabled={isEdit}
              className={`${drawerInputCls} font-mono`}
              placeholder="DRY-CLEAN"
            />
          </Field>
          <Field label="Name *">
            <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} placeholder="Dry Clean" />
          </Field>
        </div>
        <LocalizedNameFields en={nameEn} hi={nameHi} onEn={setNameEn} onHi={setNameHi} />
        <Field label="Description">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={drawerInputCls} placeholder="Optional" />
        </Field>
      </DrawerSection>

      <DrawerSection title="Presentation">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Colour hex" hint="e.g. #2F6F4F">
            <input value={colorHex} onChange={(e) => setColorHex(e.target.value)} className={`${drawerInputCls} font-mono`} placeholder="#2F6F4F" />
          </Field>
          <Field label="Display order">
            <input type="number" min="0" step="1" value={displayOrder} onChange={(e) => setDisplayOrder(e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <div className="flex flex-col gap-2">
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={isVisibleMobile} onChange={(e) => setVisibleMobile(e.target.checked)} className={checkboxCls} />
            Visible in customer mobile app
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={isVisiblePos} onChange={(e) => setVisiblePos(e.target.checked)} className={checkboxCls} />
            Visible in POS
          </label>
        </div>
        {isEdit && (
          <Field label="Status">
            <select value={status} onChange={(e) => setStatus(e.target.value)} className={drawerInputCls}>
              {STATUSES.map((s) => (<option key={s.value} value={s.value}>{s.label}</option>))}
            </select>
          </Field>
        )}
      </DrawerSection>
    </FormDrawer>
  )
}

// ══════════════════════════════════════════════════════════════════════════════
// Service
// ══════════════════════════════════════════════════════════════════════════════

interface ServiceDrawerProps {
  open: boolean
  service?: ServiceDto | null
  onClose: () => void
}

export function ServiceEditDrawer({ open, service, onClose }: ServiceDrawerProps) {
  const isEdit = !!service
  const create = useCreateService()
  const update = useUpdateService()
  const { data: catData } = useServiceCategoriesInfinite()
  const categories = catData?.pages.flatMap((p) => p.list) ?? []

  const [categoryId, setCategoryId] = useState('')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [nameEn, setNameEn] = useState('')
  const [nameHi, setNameHi] = useState('')
  const [description, setDescription] = useState('')
  const [pricingModel, setPricingModel] = useState('per_item')
  const [baseTatHours, setBaseTatHours] = useState('24')
  const [expressTatHours, setExpressTatHours] = useState('8')
  const [expressMultiplier, setExpressMultiplier] = useState('1.5')
  const [isExpressAvailable, setExpressAvailable] = useState(true)
  const [requiresInspection, setRequiresInspection] = useState(false)
  const [requiresQc, setRequiresQc] = useState(false)
  const [displayOrder, setDisplayOrder] = useState('0')
  const [status, setStatus] = useState('active')
  const [error, setError] = useState<string | null>(null)

  // Seed the form when opened or the target service changes
  // (adjust-state-while-rendering, not an effect).
  const [seededFor, setSeededFor] = useState<{ open: boolean; service: ServiceDto | null | undefined }>({ open, service })
  if ((seededFor.open !== open || seededFor.service !== service) && open) {
    setSeededFor({ open, service })
    setError(null)
    if (service) {
      const loc = parseNameLocalized(service.nameLocalized)
      setCategoryId(service.categoryId)
      setCode(service.code)
      setName(service.name)
      setNameEn(loc.en || service.name)
      setNameHi(loc.hi)
      setDescription(service.description ?? '')
      setPricingModel(service.pricingModel)
      setBaseTatHours(String(service.baseTatHours))
      setExpressTatHours(String(service.expressTatHours))
      setExpressMultiplier(String(service.expressMultiplier))
      setExpressAvailable(service.isExpressAvailable)
      setRequiresInspection(service.requiresInspection)
      setRequiresQc(service.requiresQc)
      setDisplayOrder(String(service.displayOrder))
      setStatus(service.status)
    } else {
      setCategoryId('')
      setCode('')
      setName('')
      setNameEn('')
      setNameHi('')
      setDescription('')
      setPricingModel('per_item')
      setBaseTatHours('24')
      setExpressTatHours('8')
      setExpressMultiplier('1.5')
      setExpressAvailable(true)
      setRequiresInspection(false)
      setRequiresQc(false)
      setDisplayOrder('0')
      setStatus('active')
    }
  } else if (seededFor.open !== open) {
    setSeededFor({ open, service })
  }

  if (!open) return null

  const submit = async () => {
    setError(null)
    if (!isEdit && !categoryId) return setError('Category is required.')
    if (!isEdit && !code.trim()) return setError('Code is required.')
    if (!name.trim()) return setError('Name is required.')
    if (!nameEn.trim()) return setError('Localized name (EN) is required.')
    const baseTat = Number(baseTatHours)
    const expressTat = Number(expressTatHours)
    if (!(baseTat > 0)) return setError('Base TAT must be greater than 0 hours.')
    if (isExpressAvailable && !(expressTat > 0)) return setError('Express TAT must be greater than 0 hours.')

    const common = {
      name: name.trim(),
      nameLocalized: buildNameLocalized(nameEn, nameHi),
      description: description.trim() || null,
      pricingModel,
      baseTatHours: baseTat,
      expressTatHours: expressTat,
      expressMultiplier: Number(expressMultiplier) || 1,
      isExpressAvailable,
      requiresInspection,
      requiresQc,
      iconUrl: null,
      displayOrder: Number(displayOrder) || 0,
    }
    try {
      if (isEdit && service) {
        await update.mutateAsync({ id: service.id, payload: { ...common, status } })
      } else {
        await create.mutateAsync({ ...common, categoryId, code: code.trim() })
      }
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not save the service.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Wrench}
      eyebrow="Catalog · Service"
      title={isEdit ? `Edit ${service!.name}` : 'New service'}
      width="lg"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save service' : 'Create service'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={create.isPending || update.isPending}
    >
      <DrawerSection title="Identity">
        {!isEdit && (
          <Field label="Category *">
            <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} className={drawerInputCls}>
              <option value="">Select a category…</option>
              {categories.map((c) => (<option key={c.id} value={c.id}>{c.name}</option>))}
            </select>
          </Field>
        )}
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed.' : undefined}>
            <input value={code} onChange={(e) => setCode(e.target.value)} disabled={isEdit} className={`${drawerInputCls} font-mono`} placeholder="WASH-FOLD" />
          </Field>
          <Field label="Name *">
            <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} placeholder="Wash & Fold" />
          </Field>
        </div>
        <LocalizedNameFields en={nameEn} hi={nameHi} onEn={setNameEn} onHi={setNameHi} placeholder="Wash & Fold" />
        <Field label="Description">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={drawerInputCls} placeholder="Optional" />
        </Field>
      </DrawerSection>

      <DrawerSection title="Pricing & turnaround">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Pricing model *" hint="Per-kg services are weighed at intake.">
            <select value={pricingModel} onChange={(e) => setPricingModel(e.target.value)} className={drawerInputCls}>
              {PRICING_MODELS.map((m) => (<option key={m.value} value={m.value}>{m.label}</option>))}
            </select>
          </Field>
          <Field label="Display order">
            <input type="number" min="0" step="1" value={displayOrder} onChange={(e) => setDisplayOrder(e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Base TAT (hours) *" hint="Feeds the promised-date engine.">
            <input type="number" min="1" step="1" value={baseTatHours} onChange={(e) => setBaseTatHours(e.target.value)} className={drawerInputCls} placeholder="24" />
          </Field>
          <Field label="Express TAT (hours)" hint="Promised date for express orders.">
            <input type="number" min="1" step="1" value={expressTatHours} onChange={(e) => setExpressTatHours(e.target.value)} disabled={!isExpressAvailable} className={drawerInputCls} placeholder="8" />
          </Field>
        </div>
        <Field label="Express multiplier" hint="Price surcharge factor for express, e.g. 1.5×.">
          <input type="number" min="1" step="0.05" value={expressMultiplier} onChange={(e) => setExpressMultiplier(e.target.value)} disabled={!isExpressAvailable} className={drawerInputCls} />
        </Field>
        <div className="flex flex-col gap-2">
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={isExpressAvailable} onChange={(e) => setExpressAvailable(e.target.checked)} className={checkboxCls} />
            Express available
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={requiresInspection} onChange={(e) => setRequiresInspection(e.target.checked)} className={checkboxCls} />
            Requires inspection at intake
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" checked={requiresQc} onChange={(e) => setRequiresQc(e.target.checked)} className={checkboxCls} />
            Requires QC before dispatch
          </label>
        </div>
        {isEdit && (
          <Field label="Status">
            <select value={status} onChange={(e) => setStatus(e.target.value)} className={drawerInputCls}>
              {STATUSES.map((s) => (<option key={s.value} value={s.value}>{s.label}</option>))}
            </select>
          </Field>
        )}
      </DrawerSection>
    </FormDrawer>
  )
}

// ══════════════════════════════════════════════════════════════════════════════
// Item (garment catalog: Shirt / Trousers / Saree …)
// ══════════════════════════════════════════════════════════════════════════════

interface ItemDrawerProps {
  open: boolean
  item?: ItemDto | null
  onClose: () => void
}

const IMAGE_MAX_BYTES = 5 * 1024 * 1024 // matches the backend upload validator

export function ItemEditDrawer({ open, item, onClose }: ItemDrawerProps) {
  const isEdit = !!item
  const create = useCreateItem()
  const update = useUpdateItem()
  const uploadImage = useUploadItemImage()
  const deleteImage = useDeleteItemImage()
  const { data: groupData } = useItemGroups()
  const itemGroups = groupData?.list ?? []

  const [itemGroupId, setItemGroupId] = useState('')
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [nameEn, setNameEn] = useState('')
  const [nameHi, setNameHi] = useState('')
  const [description, setDescription] = useState('')
  const [typicalWeightGrams, setTypicalWeightGrams] = useState('')
  const [requiresPerSidePrice, setRequiresPerSidePrice] = useState(false)
  const [aliases, setAliases] = useState('')
  const [displayOrder, setDisplayOrder] = useState('0')
  const [status, setStatus] = useState('active')
  const [error, setError] = useState<string | null>(null)

  // Image: a newly picked file wins over the stored one; "remove" only takes
  // effect on save so cancelling the drawer never touches the stored image.
  const [imageFile, setImageFile] = useState<File | null>(null)
  const [removeImage, setRemoveImage] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const storedImageUrl = useItemImageUrl(item?.id, open && !!item?.imageUrl)
  const pickedImageUrl = useMemo(
    () => (imageFile ? URL.createObjectURL(imageFile) : undefined),
    [imageFile],
  )
  useEffect(() => {
    return () => {
      if (pickedImageUrl) URL.revokeObjectURL(pickedImageUrl)
    }
  }, [pickedImageUrl])
  const imagePreview = pickedImageUrl ?? (removeImage ? undefined : storedImageUrl)

  // Seed the form when opened or the target item changes
  // (adjust-state-while-rendering, not an effect).
  const [seededFor, setSeededFor] = useState<{ open: boolean; item: ItemDto | null | undefined }>({ open, item })
  if ((seededFor.open !== open || seededFor.item !== item) && open) {
    setSeededFor({ open, item })
    setError(null)
    if (item) {
      const loc = parseNameLocalized(item.nameLocalized)
      setItemGroupId(item.itemGroupId ?? '')
      setCode(item.code)
      setName(item.name)
      setNameEn(loc.en || item.name)
      setNameHi(loc.hi)
      setDescription(item.description ?? '')
      setTypicalWeightGrams(item.typicalWeightGrams != null ? String(item.typicalWeightGrams) : '')
      setRequiresPerSidePrice(item.requiresPerSidePrice)
      setAliases((item.aliases ?? []).join(', '))
      setDisplayOrder(String(item.displayOrder))
      setStatus(item.status)
    } else {
      setItemGroupId('')
      setCode('')
      setName('')
      setNameEn('')
      setNameHi('')
      setDescription('')
      setTypicalWeightGrams('')
      setRequiresPerSidePrice(false)
      setAliases('')
      setDisplayOrder('0')
      setStatus('active')
    }
    setImageFile(null)
    setRemoveImage(false)
  } else if (seededFor.open !== open) {
    setSeededFor({ open, item })
  }

  if (!open) return null

  const pickImage = (file: File | null) => {
    if (!file) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      return setError('Image must be a JPEG, PNG, or WebP file.')
    }
    if (file.size > IMAGE_MAX_BYTES) {
      return setError('Image must be 5 MB or smaller.')
    }
    setError(null)
    setImageFile(file)
    setRemoveImage(false)
  }

  const clearImage = () => {
    setImageFile(null)
    setRemoveImage(true)
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  const submit = async () => {
    setError(null)
    if (!isEdit && !code.trim()) return setError('Code is required.')
    if (!name.trim()) return setError('Name is required.')
    if (!nameEn.trim()) return setError('Localized name (EN) is required.')

    const aliasList = aliases.split(',').map((a) => a.trim()).filter(Boolean)
    const common = {
      itemGroupId: itemGroupId || null,
      name: name.trim(),
      nameLocalized: buildNameLocalized(nameEn, nameHi),
      description: description.trim() || null,
      iconUrl: null,
      imageUrl: null, // null = leave unchanged; images go through the image endpoints
      typicalWeightGrams: typicalWeightGrams ? Number(typicalWeightGrams) : null,
      requiresPerSidePrice,
      aliases: aliasList.length ? aliasList : null,
      displayOrder: Number(displayOrder) || 0,
    }
    try {
      const saved =
        isEdit && item
          ? await update.mutateAsync({ id: item.id, payload: { ...common, status } })
          : await create.mutateAsync({ ...common, code: code.trim() })

      if (imageFile) {
        await uploadImage.mutateAsync({ id: saved.id, file: imageFile })
      } else if (removeImage && isEdit && item?.imageUrl) {
        await deleteImage.mutateAsync(saved.id)
      }
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, 'Could not save the item.'))
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Shirt}
      eyebrow="Catalog · Item"
      title={isEdit ? `Edit ${item!.name}` : 'New item'}
      error={error}
      onSubmit={() => void submit()}
      submitLabel={isEdit ? 'Save item' : 'Create item'}
      submittingLabel="Saving…"
      submitIcon={isEdit ? Save : Plus}
      submitting={
        create.isPending || update.isPending || uploadImage.isPending || deleteImage.isPending
      }
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Code *" hint={isEdit ? 'Code cannot be changed.' : undefined}>
            <input value={code} onChange={(e) => setCode(e.target.value)} disabled={isEdit} className={`${drawerInputCls} font-mono`} placeholder="SHIRT" />
          </Field>
          <Field label="Name *">
            <input value={name} onChange={(e) => setName(e.target.value)} className={drawerInputCls} placeholder="Shirt" />
          </Field>
        </div>
        <LocalizedNameFields en={nameEn} hi={nameHi} onEn={setNameEn} onHi={setNameHi} placeholder="Shirt" />
        <Field label="Item group">
          <select value={itemGroupId} onChange={(e) => setItemGroupId(e.target.value)} className={drawerInputCls}>
            <option value="">No group</option>
            {itemGroups.map((g) => (<option key={g.id} value={g.id}>{g.name}</option>))}
          </select>
        </Field>
        <Field label="Description">
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={drawerInputCls} placeholder="Optional" />
        </Field>
      </DrawerSection>

      <DrawerSection title="Image">
        <div className="flex items-start gap-3">
          {imagePreview ? (
            <img
              src={imagePreview}
              alt={name || 'Item'}
              className="h-20 w-20 rounded-lg border border-gray-200 object-cover"
            />
          ) : (
            <div className="flex h-20 w-20 items-center justify-center rounded-lg border border-dashed border-gray-300 bg-gray-50 text-gray-400">
              <ImagePlus className="h-6 w-6" />
            </div>
          )}
          <div className="flex flex-col items-start gap-1.5">
            <input
              ref={fileInputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              className="hidden"
              onChange={(e) => pickImage(e.target.files?.[0] ?? null)}
            />
            <div className="flex gap-2">
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                {imagePreview ? 'Replace image' : 'Choose image'}
              </button>
              {imagePreview && (
                <button
                  type="button"
                  onClick={clearImage}
                  className="rounded-lg border border-red-200 px-3 py-1.5 text-sm font-medium text-red-600 hover:bg-red-50"
                >
                  Remove
                </button>
              )}
            </div>
            <p className="text-xs text-gray-500">
              JPEG, PNG, or WebP up to 5 MB. Shown to customers in the mobile app.
            </p>
          </div>
        </div>
      </DrawerSection>

      <DrawerSection title="Attributes">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Typical weight (g)" hint="Used for per-kg estimates.">
            <input type="number" min="0" step="1" value={typicalWeightGrams} onChange={(e) => setTypicalWeightGrams(e.target.value)} className={drawerInputCls} placeholder="200" />
          </Field>
          <Field label="Display order">
            <input type="number" min="0" step="1" value={displayOrder} onChange={(e) => setDisplayOrder(e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <Field label="Aliases" hint="Comma-separated search synonyms, e.g. tee, t-shirt.">
          <input value={aliases} onChange={(e) => setAliases(e.target.value)} className={drawerInputCls} placeholder="tee, t-shirt" />
        </Field>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input type="checkbox" checked={requiresPerSidePrice} onChange={(e) => setRequiresPerSidePrice(e.target.checked)} className={checkboxCls} />
          Requires per-side pricing (e.g. two-tone garments)
        </label>
        {isEdit && (
          <Field label="Status">
            <select value={status} onChange={(e) => setStatus(e.target.value)} className={drawerInputCls}>
              {STATUSES.map((s) => (<option key={s.value} value={s.value}>{s.label}</option>))}
            </select>
          </Field>
        )}
      </DrawerSection>
    </FormDrawer>
  )
}

// ══════════════════════════════════════════════════════════════════════════════
// Delete confirm (categories / services / items share this)
// ══════════════════════════════════════════════════════════════════════════════

type Deletable = { id: string; name: string; code: string }

export function DeleteCatalogDrawer({
  entity,
  kind,
  onClose,
}: {
  entity: Deletable | null
  kind: 'category' | 'service' | 'item'
  onClose: () => void
}) {
  const delCategory = useDeleteServiceCategory()
  const delService = useDeleteService()
  const delItem = useDeleteItem()
  const [error, setError] = useState<string | null>(null)

  // Clear stale errors when a new entity is targeted (adjust-while-rendering).
  const [seededId, setSeededId] = useState<string | null>(null)
  if (entity && seededId !== entity.id) {
    setSeededId(entity.id)
    setError(null)
  }

  if (!entity) return null

  const mutation = kind === 'category' ? delCategory : kind === 'service' ? delService : delItem

  const submit = async () => {
    setError(null)
    try {
      await mutation.mutateAsync(entity.id)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, `Could not delete the ${kind}.`))
    }
  }

  return (
    <FormDrawer
      open={!!entity}
      onClose={onClose}
      icon={Trash2}
      eyebrow={<>Delete {kind} · <span className="font-mono">{entity.code}</span></>}
      title={entity.name}
      width="sm"
      error={error}
      onSubmit={() => void submit()}
      submitLabel={`Delete ${kind}`}
      submittingLabel="Deleting…"
      submitIcon={Trash2}
      submitting={mutation.isPending}
    >
      <p className="text-sm text-gray-600">
        This soft-deletes the {kind} from the catalog. It will no longer appear in lists. Existing
        orders and price-list rows are unaffected.
      </p>
    </FormDrawer>
  )
}
