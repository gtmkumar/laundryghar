import { Store, Loader2 } from 'lucide-react'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useStores } from '@/hooks/useTenancy'
import { usePosStore } from '@/stores/posStore'
import { useAuthStore } from '@/stores/authStore'
import type { StoreDto } from '@/types/api'

/**
 * Store switcher for POS topbar.
 * If the user has store_id in their JWT, they are restricted to that store
 * and we display it as a fixed label (no dropdown).
 * Platform admins and brand admins see a dropdown to pick the active POS store.
 */
export function StoreSwitcher() {
  const { user } = useAuthStore()
  const { activeStore, setActiveStore } = usePosStore()

  // If the JWT carries a store_id the user is locked to that store
  const jwtStoreId = user?.store_id

  const { data, isLoading } = useStores({})

  // Once data loads, auto-select the JWT-bound store if not already set
  const stores = data?.list ?? []

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-sm text-gray-400">
        <Loader2 className="h-4 w-4 animate-spin" />
        <span>Loading stores…</span>
      </div>
    )
  }

  // JWT-bound store: just show the name
  if (jwtStoreId) {
    const store = activeStore ?? stores.find((s) => s.id === jwtStoreId)
    if (store && !activeStore) setActiveStore(store)
    return (
      <div className="flex items-center gap-2 text-sm text-gray-600">
        <Store className="h-4 w-4" />
        <span className="font-medium">{store?.name ?? 'Store'}</span>
      </div>
    )
  }

  // Platform admin / brand admin: dropdown
  return (
    <div className="flex items-center gap-2">
      <Store className="h-4 w-4 text-gray-400" />
      <Select
        value={activeStore?.id ?? ''}
        onValueChange={(id) => {
          const store = stores.find((s) => s.id === id)
          if (store) setActiveStore(store)
        }}
      >
        <SelectTrigger className="w-44 h-9 text-sm rounded-lg">
          <SelectValue placeholder="Select store…">
            {activeStore?.name ?? 'Select store…'}
          </SelectValue>
        </SelectTrigger>
        <SelectContent>
          {stores.map((store: StoreDto) => (
            <SelectItem key={store.id} value={store.id}>
              {store.name}
            </SelectItem>
          ))}
          {stores.length === 0 && (
            <div className="px-4 py-2 text-xs text-gray-400">No stores found</div>
          )}
        </SelectContent>
      </Select>
    </div>
  )
}
