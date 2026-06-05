/**
 * Catalog API — maps to CustomerEndpoints.cs (Catalog service)
 * Endpoint prefix: {Catalog}/api/v1/customer/
 */
import { catalogClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  CustomerAddressDto,
  CustomerProfileDto,
  ListResponse,
  PatchProfileRequest,
  PriceListItemDto,
  ServiceCategoryDto,
  ServiceDto,
  SingleResponse,
} from '@/types/api';

// ── Catalog reads ────────────────────────────────────────────────────────────

/** GET /api/v1/customer/catalog/categories */
export async function getCategories(): Promise<ServiceCategoryDto[]> {
  const res = await catalogClient.get<ListResponse<ServiceCategoryDto>>(
    '/customer/catalog/categories',
  );
  return unwrapList(res.data);
}

/** GET /api/v1/customer/catalog/services?categoryId= */
export async function getServices(categoryId?: string): Promise<ServiceDto[]> {
  const res = await catalogClient.get<ListResponse<ServiceDto>>(
    '/customer/catalog/services',
    { params: categoryId ? { categoryId } : undefined },
  );
  return unwrapList(res.data);
}

/** GET /api/v1/customer/catalog/price-list */
export async function getPriceList(): Promise<PriceListItemDto[]> {
  const res = await catalogClient.get<ListResponse<PriceListItemDto>>(
    '/customer/catalog/price-list',
  );
  return unwrapList(res.data);
}

// ── Profile ──────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/profile */
export async function getProfile(): Promise<CustomerProfileDto> {
  const res = await catalogClient.get<SingleResponse<CustomerProfileDto>>(
    '/customer/profile/',
  );
  return unwrapSingle(res.data);
}

/** PATCH /api/v1/customer/profile */
export async function patchProfile(
  body: PatchProfileRequest,
): Promise<CustomerProfileDto> {
  const res = await catalogClient.patch<SingleResponse<CustomerProfileDto>>(
    '/customer/profile/',
    body,
  );
  return unwrapSingle(res.data);
}

// ── Addresses ────────────────────────────────────────────────────────────────

/** GET /api/v1/customer/addresses */
export async function getAddresses(): Promise<CustomerAddressDto[]> {
  const res = await catalogClient.get<ListResponse<CustomerAddressDto>>(
    '/customer/addresses/',
  );
  return unwrapList(res.data);
}
