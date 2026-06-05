import { useState } from 'react'
import { Building2, ChevronDown, Loader2 } from 'lucide-react'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useBrands } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import { useAuthStore } from '@/stores/authStore'
import type { BrandDto } from '@/types/api'

/**
 * Brand switcher shown in the topbar.
 *
 * Visibility rules:
 * - platform_admin: shown, can pick any brand from the list.
 * - brand_admin / franchise_owner / store_admin: hidden — brand is fixed via JWT.
 *
 * The selected brand id is stored in brandStore and sent as X-Brand-Id on all
 * brand-scoped API requests via the axios interceptor.
 */
export function BrandSwitcher() {
  const { user } = useAuthStore()
  const { activeBrandId, setActiveBrand, activeBrand } = useBrandStore()
  const [open, setOpen] = useState(false)

  const isPlatformAdmin = user?.user_type === 'platform_admin'

  // Only fetch brands list for platform admins
  const { data, isLoading } = useBrands(
    {},
    // Only query when this switcher is rendered (for platform admins)
  )

  if (!isPlatformAdmin) {
    // Non-platform users: show their fixed brand name from the JWT context
    if (!activeBrand) return null
    return (
      <div className="flex items-center gap-2 text-sm text-gray-600">
        <Building2 className="h-4 w-4" />
        <span>{activeBrand.name}</span>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-sm text-gray-400">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span>Loading brands…</span>
      </div>
    )
  }

  const brands = data?.list ?? []

  return (
    <div className="flex items-center gap-2">
      <Building2 className="h-4 w-4 text-gray-400" />
      <Select
        value={activeBrandId ?? ''}
        onValueChange={(id) => {
          const brand = brands.find((b) => b.id === id)
          if (brand) setActiveBrand(brand)
          setOpen(false)
        }}
        open={open}
        onOpenChange={setOpen}
      >
        <SelectTrigger className="w-48 h-8 text-sm">
          <SelectValue placeholder="Select brand…">
            {activeBrand?.name ?? 'Select brand…'}
          </SelectValue>
          <ChevronDown className="h-3.5 w-3.5 ml-auto opacity-50" />
        </SelectTrigger>
        <SelectContent>
          {brands.map((brand: BrandDto) => (
            <SelectItem key={brand.id} value={brand.id}>
              {brand.name}
            </SelectItem>
          ))}
          {brands.length === 0 && (
            <div className="px-4 py-2 text-xs text-gray-400">No brands found</div>
          )}
        </SelectContent>
      </Select>
    </div>
  )
}
