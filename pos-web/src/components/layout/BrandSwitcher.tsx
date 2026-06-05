import { Building2, Loader2 } from 'lucide-react'
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
 * Brand switcher shown in the POS topbar.
 *
 * Visibility rules:
 * - platform_admin: shown, can pick any brand from the list.
 * - brand_admin / store_admin / store_staff: hidden — brand is fixed via JWT.
 */
export function BrandSwitcher() {
  const { user } = useAuthStore()
  const { activeBrandId, setActiveBrand, activeBrand } = useBrandStore()

  const isPlatformAdmin = user?.user_type === 'platform_admin'

  const { data, isLoading } = useBrands({})

  if (!isPlatformAdmin) {
    if (!activeBrand) return null
    return (
      <div className="flex items-center gap-2 text-sm text-gray-600">
        <Building2 className="h-4 w-4" />
        <span className="font-medium">{activeBrand.name}</span>
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
        }}
      >
        <SelectTrigger className="w-44 h-9 text-sm rounded-lg">
          <SelectValue placeholder="Select brand…">
            {activeBrand?.name ?? 'Select brand…'}
          </SelectValue>
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
