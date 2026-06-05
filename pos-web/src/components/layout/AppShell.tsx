import { Outlet } from 'react-router-dom'
import { Topbar } from './Topbar'
import { BottomNav } from './BottomNav'

/**
 * POS App Shell.
 * Layout: fixed Topbar at top, scrollable content area, fixed BottomNav for touch navigation.
 * Optimized for landscape tablet use at a walk-in counter.
 */
export function AppShell() {
  return (
    <div className="flex flex-col h-screen overflow-hidden bg-gray-50">
      <Topbar />
      <main className="flex-1 overflow-y-auto">
        <Outlet />
      </main>
      <BottomNav />
    </div>
  )
}
