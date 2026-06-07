import { useQuery } from '@tanstack/react-query'
import { getWarehouseBoard } from '@/api/warehouse'
import { useEffectiveBrandId } from './useBrandContext'

/**
 * Live warehouse kanban — refetches every 30s. Gated on the effective brand id
 * so the request always carries brand context (the board renders outside the
 * AppShell, where platform_admin's brand is otherwise auto-selected).
 */
export function useWarehouseBoard() {
  const brandId = useEffectiveBrandId()
  return useQuery({
    queryKey: ['warehouse', 'board', brandId],
    queryFn: getWarehouseBoard,
    enabled: !!brandId,
    refetchInterval: 30_000,
  })
}
