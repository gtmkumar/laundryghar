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
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-[200] focus:rounded-lg focus:bg-blue-600 focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-white"
      >
        Skip to content
      </a>
      <Topbar />
      <main id="main-content" className="flex-1 overflow-y-auto">
        <Outlet />
      </main>
      <BottomNav />
    </div>
  )
}
