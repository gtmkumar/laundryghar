/**
 * The coarse account user_type vocabulary, mirroring the backend
 * laundryghar.SharedDataModel/Enums/UserType. Keep in sync with that file.
 *
 * `warehouse_staff` is the laundry-specific on-site staff type retained for data compatibility;
 * `ops_staff` is the vertical-neutral successor used by salon/logistics. See verticalTerms for which
 * one a given brand should use.
 */
export const UserType = {
  platformAdmin: 'platform_admin',
  brandAdmin: 'brand_admin',
  franchiseOwner: 'franchise_owner',
  storeAdmin: 'store_admin',
  staff: 'staff',
  warehouseStaff: 'warehouse_staff',
  opsStaff: 'ops_staff',
  rider: 'rider',
  auditor: 'auditor',
  support: 'support',
} as const

export type UserTypeKey = (typeof UserType)[keyof typeof UserType]

/** Human-readable labels for each user_type — vertical-neutral. */
export const USER_TYPE_LABEL: Record<string, string> = {
  platform_admin: 'Platform admin',
  brand_admin: 'Brand admin',
  franchise_owner: 'Franchise owner',
  store_admin: 'Store admin',
  staff: 'Staff',
  warehouse_staff: 'Warehouse staff',
  ops_staff: 'Operations staff',
  rider: 'Rider',
  auditor: 'Auditor',
  support: 'Support',
}

/** Humanise a user_type code, falling back to the raw code if unknown. */
export function userTypeLabel(userType: string | null | undefined): string {
  if (!userType) return '—'
  return USER_TYPE_LABEL[userType] ?? userType
}
