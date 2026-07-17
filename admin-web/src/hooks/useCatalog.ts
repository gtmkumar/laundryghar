import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import {
  getServiceCategories,
  getServices,
  getItems,
  getFabricTypes,
  getItemGroups,
  getPriceLists,
  getPriceListItems,
  getAdminCustomers,
  updateAdminCustomer,
  deleteAdminCustomer,
  createServiceCategory,
  updateServiceCategory,
  deleteServiceCategory,
  createService,
  updateService,
  deleteService,
  createItem,
  updateItem,
  deleteItem,
  uploadItemImage,
  deleteItemImage,
  getItemImageBlob,
  createPriceList,
  updatePriceList,
  publishPriceList,
  deletePriceList,
  createPriceListItem,
  updatePriceListItem,
  createFabricType,
  updateFabricType,
  getAddOns,
  createAddOn,
  updateAddOn,
  deleteAddOn,
  getPricingMatrix,
  getPricingHistory,
  revertPricingChange,
  getManagedItems,
  getItemStats,
  saveItemPricing,
  importItems,
  parseImportFile,
  parseGoogleSheet,
  createItemGroup,
  exportPriceList,
  getValueSlabs,
  createValueSlab,
  updateValueSlab,
  deleteValueSlab,
} from '@/api/catalog'
import { removeListItem, rollbackWithToast, snapshotAndSet } from '@/lib/optimistic'
import type {
  PricingMatrix,
  PaginationParams,
  CreateFabricTypePayload,
  UpdateFabricTypePayload,
  CreateAddOnPayload,
  UpdateAddOnPayload,
  AdminCustomerListParams,
  AdminUpdateCustomerPayload,
  CreateServiceCategoryPayload,
  UpdateServiceCategoryPayload,
  CreateServicePayload,
  UpdateServicePayload,
  CreateItemPayload,
  UpdateItemPayload,
  SaveItemPricingPayload,
  ImportItemsPayload,
  ParseGoogleSheetPayload,
  CreateItemGroupPayload,
  CreatePriceListPayload,
  UpdatePriceListPayload,
  CreatePriceListItemPayload,
  UpdatePriceListItemPayload,
  CreateValueSlabPayload,
  UpdateValueSlabPayload,
} from '@/types/api'

export const catalogKeys = {
  categories: (params?: object) => ['catalog', 'categories', params] as const,
  categoriesInfinite: (params?: object) => ['catalog', 'categories', 'infinite', params] as const,
  services: (params?: object) => ['catalog', 'services', params] as const,
  servicesInfinite: (params?: object) => ['catalog', 'services', 'infinite', params] as const,
  items: (params?: object) => ['catalog', 'items', params] as const,
  itemsInfinite: (params?: object) => ['catalog', 'items', 'infinite', params] as const,
  itemImage: (itemId: string) => ['catalog', 'item-image', itemId] as const,
  fabricTypes: (params?: object) => ['catalog', 'fabricTypes', params] as const,
  itemGroups: (params?: object) => ['catalog', 'itemGroups', params] as const,
  priceLists: (params?: object) => ['catalog', 'priceLists', params] as const,
  priceListsInfinite: (params?: object) => ['catalog', 'priceLists', 'infinite', params] as const,
  priceListItems: (priceListId: string) => ['catalog', 'priceListItems', priceListId] as const,
  addOns: (params?: object) => ['catalog', 'addOns', params] as const,
  adminCustomers: (params?: object) => ['catalog', 'adminCustomers', params] as const,
  valueSlabs: (params?: object) => ['catalog', 'valueSlabs', params] as const,
}

const CATALOG_PAGE_SIZE = 100

export function useServiceCategories(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.categories(params),
    queryFn: () => getServiceCategories(params),
  })
}

export function useServiceCategoriesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.categoriesInfinite(),
    queryFn: ({ pageParam }) =>
      getServiceCategories({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useServices(params: PaginationParams & { categoryId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.services(params),
    queryFn: () => getServices(params),
  })
}

export function useServicesInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.servicesInfinite(),
    queryFn: ({ pageParam }) =>
      getServices({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

export function useItems(params: PaginationParams & { itemGroupId?: string } = {}) {
  return useQuery({
    queryKey: catalogKeys.items(params),
    queryFn: () => getItems(params),
  })
}

export function useItemsInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.itemsInfinite(),
    queryFn: ({ pageParam }) => getItems({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

/** Managed items (item + per-service prices + fabric variants) for the Items page. */
export function useManagedItems(params: { itemGroupId?: string; search?: string } = {}) {
  return useQuery({
    queryKey: ['catalog', 'managedItems', params],
    queryFn: () => getManagedItems(params),
    staleTime: 30_000,
  })
}

export function useItemStats() {
  return useQuery({
    queryKey: ['catalog', 'itemStats'],
    queryFn: () => getItemStats(),
    staleTime: 30_000,
  })
}

export function useSaveItemPricing() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: SaveItemPricingPayload }) => saveItemPricing(v.id, v.payload),
    // Optimistic: write the new base prices straight into every cached pricing
    // matrix so the inline matrix edit (and its fabric columns, computed
    // client-side from base × multiplier) updates on commit, not on response.
    onMutate: async ({ id, payload }) => {
      const priceByService = new Map(
        payload.servicePrices
          .filter((sp): sp is { serviceId: string; basePrice: number } => sp.basePrice != null)
          .map((sp) => [sp.serviceId, sp.basePrice]),
      )
      if (priceByService.size === 0) return undefined
      return snapshotAndSet(qc, [['catalog', 'pricingMatrix']], (data) => {
        const matrix = data as PricingMatrix
        if (!Array.isArray(matrix?.rows)) return data
        return {
          ...matrix,
          rows: matrix.rows.map((row) =>
            row.itemId === id && priceByService.has(row.serviceId)
              ? { ...row, basePrice: priceByService.get(row.serviceId)! }
              : row,
          ),
        }
      })
    },
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['catalog', 'managedItems'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'itemStats'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingMatrix'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingHistory'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'priceListItems'] })
    },
  })
}

export function useImportItems() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: ImportItemsPayload) => importItems(payload),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: ['catalog', 'managedItems'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'itemStats'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'items'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingMatrix'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingHistory'] })
      // Auto-created categories add new item groups (the Items page category rail).
      if (result.categoriesCreated > 0) {
        qc.invalidateQueries({ queryKey: ['catalog', 'itemGroups'] })
      }
    },
  })
}

/**
 * Dry-run parse of an upload — no cache invalidation (it doesn't write). Exposed
 * as a mutation for the wizard's loading/progress/error state. The variables
 * carry the file and an optional upload-progress callback.
 */
export function useParseImportFile() {
  return useMutation({
    mutationFn: (v: { file: File; onProgress?: (pct: number) => void }) =>
      parseImportFile(v.file, v.onProgress),
  })
}

/**
 * Dry-run parse of a published Google Sheet — like {@link useParseImportFile} but
 * sourced from a link, so there's no upload progress and (again) no invalidation.
 */
export function useParseGoogleSheet() {
  return useMutation({
    mutationFn: (payload: ParseGoogleSheetPayload) => parseGoogleSheet(payload),
  })
}

/** Flat fabric-type lookup (first 100) — feeds the price-list item editor. */
export function useFabricTypes() {
  return useQuery({
    queryKey: catalogKeys.fabricTypes(),
    queryFn: () => getFabricTypes(),
    staleTime: 5 * 60_000,
  })
}

export function useCreateFabricType() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateFabricTypePayload) => createFabricType(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'fabricTypes'] }),
  })
}
export function useUpdateFabricType() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: UpdateFabricTypePayload }) => updateFabricType(v.id, v.payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'fabricTypes'] }),
  })
}

export function usePricingMatrix(storeId?: string) {
  return useQuery({
    queryKey: ['catalog', 'pricingMatrix', storeId ?? 'all'],
    queryFn: () => getPricingMatrix(storeId),
    staleTime: 60_000,
  })
}

export function usePricingHistory() {
  return useQuery({ queryKey: ['catalog', 'pricingHistory'], queryFn: () => getPricingHistory(), staleTime: 30_000 })
}
export function useRevertPricingChange() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => revertPricingChange(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingHistory'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'fabricTypes'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'pricingMatrix'] })
    },
  })
}

// ── Add-ons / surcharges ──────────────────────────────────────────────────────
export function useAddOns() {
  return useQuery({ queryKey: catalogKeys.addOns(), queryFn: () => getAddOns(), staleTime: 60_000 })
}
export function useCreateAddOn() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateAddOnPayload) => createAddOn(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'addOns'] }),
  })
}
export function useUpdateAddOn() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: UpdateAddOnPayload }) => updateAddOn(v.id, v.payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'addOns'] }),
  })
}
export function useDeleteAddOn() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteAddOn(id),
    onMutate: (id) => removeListItem(qc, [['catalog', 'addOns']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => qc.invalidateQueries({ queryKey: ['catalog', 'addOns'] }),
  })
}

// ── Value slabs (declared-garment-value pricing) ──────────────────────────────

export function useValueSlabs(params: { serviceId?: string; includeArchived?: boolean } = {}) {
  return useQuery({
    queryKey: catalogKeys.valueSlabs(params),
    queryFn: () => getValueSlabs(params),
    staleTime: 30_000,
  })
}

export function useCreateValueSlab() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateValueSlabPayload) => createValueSlab(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'valueSlabs'] }),
  })
}

export function useUpdateValueSlab() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (v: { id: string; payload: UpdateValueSlabPayload }) => updateValueSlab(v.id, v.payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog', 'valueSlabs'] }),
  })
}

export function useDeleteValueSlab() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteValueSlab(id),
    // DELETE archives the slab; the default (non-archived) view drops the row,
    // so optimistic removal matches. onSettled reconciles the includeArchived view.
    onMutate: (id) => removeListItem(qc, [['catalog', 'valueSlabs']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => qc.invalidateQueries({ queryKey: ['catalog', 'valueSlabs'] }),
  })
}

// ── Price-list export ─────────────────────────────────────────────────────────

/**
 * Downloads a price list as CSV/Excel and triggers a browser save. Exposed as a
 * mutation (no cache writes) purely for its pending/error state on the export
 * buttons; the blob→anchor download side-effect lives in onSuccess.
 */
export function useExportPriceList() {
  return useMutation({
    mutationFn: (v: { id: string; format: 'csv' | 'xlsx' }) => exportPriceList(v.id, v.format),
    onSuccess: ({ blob, filename }) => {
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      a.click()
      URL.revokeObjectURL(url)
    },
  })
}

export function useCreateItemGroup() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateItemGroupPayload) => createItemGroup(payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['catalog', 'itemGroups'] })
      qc.invalidateQueries({ queryKey: ['catalog', 'itemStats'] })
    },
  })
}

/** Flat item-group lookup (first 100) — feeds item + price-list item editors. */
export function useItemGroups() {
  return useQuery({
    queryKey: catalogKeys.itemGroups(),
    queryFn: () => getItemGroups(),
    staleTime: 5 * 60_000,
  })
}

export function usePriceLists(params: PaginationParams = {}) {
  return useQuery({
    queryKey: catalogKeys.priceLists(params),
    queryFn: () => getPriceLists(params),
  })
}

export function usePriceListsInfinite() {
  return useInfiniteQuery({
    queryKey: catalogKeys.priceListsInfinite(),
    queryFn: ({ pageParam }) =>
      getPriceLists({ page: pageParam, pageSize: CATALOG_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
  })
}

/** Priced rows inside one price list. */
export function usePriceListItems(priceListId: string | null) {
  return useQuery({
    queryKey: catalogKeys.priceListItems(priceListId ?? ''),
    queryFn: () => getPriceListItems(priceListId as string),
    enabled: !!priceListId,
  })
}

// ── Catalog mutations ─────────────────────────────────────────────────────────

function invalidate(qc: ReturnType<typeof useQueryClient>, ...keys: readonly (readonly unknown[])[]) {
  for (const key of keys) void qc.invalidateQueries({ queryKey: key })
}

export function useCreateServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateServiceCategoryPayload) => createServiceCategory(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useUpdateServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateServiceCategoryPayload }) =>
      updateServiceCategory(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useDeleteServiceCategory() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteServiceCategory(id),
    // Prefix covers the plain + infinite category lists (both under ['catalog','categories']).
    onMutate: (id) => removeListItem(qc, [['catalog', 'categories']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidate(qc, ['catalog', 'categories']),
  })
}

export function useCreateService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateServicePayload) => createService(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useUpdateService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateServicePayload }) =>
      updateService(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useDeleteService() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteService(id),
    // Prefix covers the plain + infinite service lists (both under ['catalog','services']).
    onMutate: (id) => removeListItem(qc, [['catalog', 'services']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidate(qc, ['catalog', 'services']),
  })
}

export function useCreateItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreateItemPayload) => createItem(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'items'], ['catalog', 'managedItems'], ['catalog', 'itemStats']),
  })
}

export function useUpdateItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateItemPayload }) =>
      updateItem(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'items'], ['catalog', 'managedItems'], ['catalog', 'itemStats']),
  })
}

export function useDeleteItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteItem(id),
    // The Items page renders managedItems, so remove from both that list and the
    // plain/infinite items lists (all keyed by a shared id).
    onMutate: (id) => removeListItem(qc, [['catalog', 'items'], ['catalog', 'managedItems']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidate(qc, ['catalog', 'items'], ['catalog', 'managedItems'], ['catalog', 'itemStats']),
  })
}

export function useUploadItemImage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => uploadItemImage(id, file),
    onSuccess: (_, { id }) =>
      invalidate(qc, ['catalog', 'items'], catalogKeys.itemImage(id)),
  })
}

export function useDeleteItemImage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteItemImage(id),
    onSuccess: (_, id) => invalidate(qc, ['catalog', 'items'], catalogKeys.itemImage(id)),
  })
}

/**
 * Object URL for an item's stored image, fetched through the authenticated
 * streaming endpoint (a plain <img src> can't carry the bearer token).
 * Returns undefined while loading or when the item has no stored image.
 */
export function useItemImageUrl(itemId: string | undefined, hasImage: boolean) {
  const { data: blob } = useQuery({
    queryKey: catalogKeys.itemImage(itemId ?? ''),
    queryFn: () => getItemImageBlob(itemId!),
    enabled: !!itemId && hasImage,
    staleTime: 5 * 60 * 1000,
  })

  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : undefined), [blob])
  useEffect(() => {
    return () => {
      if (url) URL.revokeObjectURL(url)
    }
  }, [url])

  return url
}

// ── Pricing mutations ─────────────────────────────────────────────────────────

export function useCreatePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePriceListPayload) => createPriceList(payload),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useUpdatePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePriceListPayload }) =>
      updatePriceList(id, payload),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function usePublishPriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => publishPriceList(id),
    onSuccess: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useDeletePriceList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deletePriceList(id),
    // Prefix covers the plain + infinite price-list caches (both under ['catalog','priceLists']).
    onMutate: (id) => removeListItem(qc, [['catalog', 'priceLists']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => invalidate(qc, ['catalog', 'priceLists']),
  })
}

export function useCreatePriceListItem(priceListId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (payload: CreatePriceListItemPayload) => createPriceListItem(priceListId, payload),
    onSuccess: () => invalidate(qc, catalogKeys.priceListItems(priceListId)),
  })
}

export function useUpdatePriceListItem(priceListId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdatePriceListItemPayload }) =>
      updatePriceListItem(priceListId, id, payload),
    onSuccess: () => invalidate(qc, catalogKeys.priceListItems(priceListId)),
  })
}

// ── Admin Customers ───────────────────────────────────────────────────────────

export function useAdminCustomers(params: AdminCustomerListParams = {}, enabled = true) {
  return useQuery({
    queryKey: catalogKeys.adminCustomers(params),
    queryFn: () => getAdminCustomers(params),
    enabled,
    // Customer names change rarely — 5 min stale time is fine
    staleTime: 5 * 60_000,
  })
}

const CUSTOMERS_PAGE_SIZE = 50

/**
 * Infinite-scroll customers list (house pattern: useInfiniteQuery + the
 * useInfiniteScroll sentinel). Replaces the hard-capped pageSize-100 fetch so
 * brands with thousands of customers don't silently lose rows past the cap.
 * Flatten `data.pages.flatMap(p => p.list)` for the table; `totalCount` and
 * `hasNextPage` come off the last page.
 */
export function useAdminCustomersInfinite(
  params: Omit<AdminCustomerListParams, 'page' | 'pageSize'> = {},
  enabled = true,
) {
  return useInfiniteQuery({
    queryKey: catalogKeys.adminCustomers({ ...params, _infinite: true }),
    queryFn: ({ pageParam }) =>
      getAdminCustomers({ ...params, page: pageParam, pageSize: CUSTOMERS_PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.hasNextPage ? allPages.length + 1 : undefined,
    enabled,
    staleTime: 5 * 60_000,
  })
}

export function useUpdateAdminCustomer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: AdminUpdateCustomerPayload }) =>
      updateAdminCustomer(id, payload),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['catalog', 'adminCustomers'] })
    },
  })
}

export function useDeleteAdminCustomer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => deleteAdminCustomer(id),
    // Prefix covers the plain + infinite customer lists (both under ['catalog','adminCustomers']).
    onMutate: (id) => removeListItem(qc, [['catalog', 'adminCustomers']], id),
    onError: (error, _v, ctx) => rollbackWithToast(ctx, error),
    onSettled: () => void qc.invalidateQueries({ queryKey: ['catalog', 'adminCustomers'] }),
  })
}

/**
 * Returns a stable Map<customerId, displayName> derived from the first page of
 * admin customers (pageSize 100). Falls back gracefully if the query is still
 * loading or returns no data. Used in the dashboard live-order feed.
 */
export function useCustomerNameMap(enabled = true): Map<string, string> {
  const { data } = useAdminCustomers({ pageSize: 100 }, enabled)

  return useMemo(() => {
    const map = new Map<string, string>()
    for (const c of data?.list ?? []) {
      // Prefer displayName, then firstName+lastName, then customerCode
      const name =
        c.displayName?.trim() ||
        [c.firstName, c.lastName].filter(Boolean).join(' ').trim() ||
        c.customerCode
      map.set(c.id, name)
    }
    return map
  }, [data])
}
