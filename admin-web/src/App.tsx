import type { ComponentType } from 'react'
import {
  createBrowserRouter,
  RouterProvider,
  Navigate,
} from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'

import { AppShell } from '@/components/layout/AppShell'
import { ProtectedRoute } from '@/components/layout/ProtectedRoute'
import { Toaster } from '@/components/shared/Toaster'

// Route-level code splitting: each page is its own chunk, loaded on first
// visit instead of shipping every page in the startup bundle. Pages use
// named exports, so map them onto the Component key react-router expects.
function lazyPage(
  load: () => Promise<Record<string, unknown>>,
  name: string,
) {
  return async () => ({
    Component: (await load())[name] as ComponentType,
  })
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

const router = createBrowserRouter([
  {
    path: '/login',
    lazy: lazyPage(() => import('@/pages/auth/LoginPage'), 'LoginPage'),
  },
  {
    path: '/accept-invite',
    lazy: lazyPage(() => import('@/pages/auth/AcceptInvitePage'), 'AcceptInvitePage'),
  },
  {
    element: <ProtectedRoute />,
    children: [
      // Full-screen operator board — no admin sidebar/topbar shell.
      {
        path: 'warehouse/board',
        lazy: lazyPage(() => import('@/pages/warehouse/WarehouseBoardPage'), 'WarehouseBoardPage'),
      },
      {
        element: <AppShell />,
        children: [
          { index: true,            lazy: lazyPage(() => import('@/pages/DashboardPage'), 'DashboardPage') },
          { path: 'tenancy',        lazy: lazyPage(() => import('@/pages/tenancy/TenancyPage'), 'TenancyPage') },
          { path: 'catalog',        lazy: lazyPage(() => import('@/pages/catalog/CatalogPage'), 'CatalogPage') },
          { path: 'orders',         lazy: lazyPage(() => import('@/pages/orders/OrdersPage'), 'OrdersPage') },
          { path: 'cms',            lazy: lazyPage(() => import('@/pages/cms/CmsPage'), 'CmsPage') },
          { path: 'analytics',      lazy: lazyPage(() => import('@/pages/analytics/AnalyticsPage'), 'AnalyticsPage') },
          { path: 'access-control', lazy: lazyPage(() => import('@/pages/access-control/AccessControlPage'), 'AccessControlPage') },
          { path: 'settings',       lazy: lazyPage(() => import('@/pages/settings/SettingsPage'), 'SettingsPage') },
          { path: 'customers',      lazy: lazyPage(() => import('@/pages/customers/CustomersPage'), 'CustomersPage') },
          { path: 'riders',         lazy: lazyPage(() => import('@/pages/riders/RidersPage'), 'RidersPage') },
          { path: 'packages',       lazy: lazyPage(() => import('@/pages/packages/PackagesPage'), 'PackagesPage') },
          { path: 'coupons',        lazy: lazyPage(() => import('@/pages/coupons/CouponsPage'), 'CouponsPage') },
          { path: 'subscriptions',  lazy: lazyPage(() => import('@/pages/subscriptions/SubscriptionsPage'), 'SubscriptionsPage') },
          { path: 'cashbook',       lazy: lazyPage(() => import('@/pages/finance/CashBookPage'), 'CashBookPage') },
          { path: 'expenses',       lazy: lazyPage(() => import('@/pages/finance/ExpensesPage'), 'ExpensesPage') },
          { path: 'royalty',        lazy: lazyPage(() => import('@/pages/finance/RoyaltyPage'), 'RoyaltyPage') },
          { path: 'platform-plans', lazy: lazyPage(() => import('@/pages/finance/PlatformPlansPage'), 'PlatformPlansPage') },
          { path: '*',              element: <Navigate to="/" replace /> },
        ],
      },
    ],
  },
])

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <Toaster />
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  )
}
