/**
 * Slim app-wide offline indicator (R3-POS-5). Renders nothing while online; when
 * the tablet loses connectivity it shows a persistent amber strip so staff know
 * actions won't reach the server. The in-progress cart is persisted locally, so
 * the basket survives the outage — only submit is blocked (see NewOrderPage).
 */
import { WifiOff } from 'lucide-react'
import { useNetworkStatus } from '@/hooks/useNetworkStatus'

export function OfflineBanner() {
  const online = useNetworkStatus()
  if (online) return null

  return (
    <div
      role="status"
      aria-live="polite"
      className="no-print flex items-center justify-center gap-2 bg-amber-500 px-4 py-1.5 text-xs font-semibold text-white shrink-0"
    >
      <WifiOff className="h-4 w-4" />
      You're offline — changes won't save until the connection is back.
    </div>
  )
}
