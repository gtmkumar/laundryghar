/**
 * Catalog API — maps to CustomerEndpoints.cs (Catalog service)
 * Endpoint prefix: {Catalog}/api/v1/customer/
 */
import { catalogClient, unwrapList, unwrapSingle } from '@/api/client';
import type {
  CreateAddressRequest,
  CustomerAddressDto,
  CustomerProfileDto,
  ListResponse,
  PatchProfileRequest,
  PriceListItemDto,
  ServiceCategoryDto,
  ServiceabilityDto,
  ServiceDto,
  SingleResponse,
  UpdateAddressRequest,
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

/** POST /api/v1/customer/addresses */
export async function createAddress(
  body: CreateAddressRequest,
): Promise<CustomerAddressDto> {
  const res = await catalogClient.post<SingleResponse<CustomerAddressDto>>(
    '/customer/addresses/',
    body,
  );
  return unwrapSingle(res.data);
}

/** PUT /api/v1/customer/addresses/{id} */
export async function updateAddress(
  id: string,
  body: UpdateAddressRequest,
): Promise<CustomerAddressDto> {
  const res = await catalogClient.put<SingleResponse<CustomerAddressDto>>(
    `/customer/addresses/${id}`,
    body,
  );
  return unwrapSingle(res.data);
}

/** DELETE /api/v1/customer/addresses/{id} */
export async function deleteAddress(id: string): Promise<void> {
  await catalogClient.delete(`/customer/addresses/${id}`);
}

/** GET /api/v1/customer/serviceability?pincode= */
export async function checkServiceability(
  pincode: string,
): Promise<ServiceabilityDto> {
  const res = await catalogClient.get<SingleResponse<ServiceabilityDto>>(
    '/customer/serviceability/',
    { params: { pincode } },
  );
  return unwrapSingle(res.data);
}
