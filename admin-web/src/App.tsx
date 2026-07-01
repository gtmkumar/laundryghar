import type { ComponentType } from 'react'
import {
  createBrowserRouter,
  RouterProvider,
  Navigate,
} from 'react-router-dom'
import { MutationCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { showToast } from '@/stores/toastStore'
import { apiErrorMessage, apiErrorStatus } from '@/lib/apiError'

import { AppShell } from '@/components/layout/AppShell'
import { ProtectedRoute } from '@/components/layout/ProtectedRoute'
import { RequirePermission } from '@/components/layout/RequirePermission'
import { Toaster } from '@/components/shared/Toaster'
import { StepUpDialog } from '@/components/shared/StepUpDialog'

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
  // Global safety net: NO failed mutation is ever silent. Drawers additionally
  // render inline banners / per-field errors via FormDrawer, but even a screen
  // that forgot to wire error handling gets a visible toast with the backend's
  // responseMessage / validator text (409 duplicate, 422 validation, 404 …).
  // 401 is excluded (handled by the silent refresh-and-retry interceptor) and
  // 403 is excluded (the axios interceptor already shows a permission toast).
  mutationCache: new MutationCache({
    onError: (error) => {
      const status = apiErrorStatus(error)
      if (status === 401 || status === 403) return
      showToast('error', apiErrorMessage(error))
    },
  }),
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
      // Full-screen operator board — no admin sidebar/topbar shell. Still
      // permission-gated (garment.read) via the RequirePermission layer.
      {
        element: <RequirePermission />,
        children: [
          {
            path: 'warehouse/board',
            lazy: lazyPage(() => import('@/pages/warehouse/WarehouseBoardPage'), 'WarehouseBoardPage'),
          },
        ],
      },
      {
        element: <AppShell />,
        children: [
          // Per-route permission gate (mirrors the server modules table).
          // Unauthorized deep-links render a 403 page, not an exploding screen.
          {
            element: <RequirePermission />,
            children: [
              { index: true,            lazy: lazyPage(() => import('@/pages/DashboardPage'), 'DashboardPage') },
              { path: 'tenancy',        lazy: lazyPage(() => import('@/pages/tenancy/TenancyPage'), 'TenancyPage') },
              { path: 'items',          lazy: lazyPage(() => import('@/pages/items/ItemsPage'), 'ItemsPage') },
              { path: 'catalog',        lazy: lazyPage(() => import('@/pages/catalog/CatalogPage'), 'CatalogPage') },
              { path: 'orders',         lazy: lazyPage(() => import('@/pages/orders/OrdersPage'), 'OrdersPage') },
              { path: 'cms',            lazy: lazyPage(() => import('@/pages/cms/CmsPage'), 'CmsPage') },
              { path: 'analytics',      lazy: lazyPage(() => import('@/pages/analytics/AnalyticsPage'), 'AnalyticsPage') },
              { path: 'access-control', lazy: lazyPage(() => import('@/pages/access-control/AccessControlPage'), 'AccessControlPage') },
              { path: 'settings',       lazy: lazyPage(() => import('@/pages/settings/SettingsPage'), 'SettingsPage') },
              { path: 'customers',      lazy: lazyPage(() => import('@/pages/customers/CustomersPage'), 'CustomersPage') },
              { path: 'support',        lazy: lazyPage(() => import('@/pages/support/SupportInboxPage'), 'SupportInboxPage') },
              { path: 'riders',         lazy: lazyPage(() => import('@/pages/riders/RidersPage'), 'RidersPage') },
              { path: 'riders/verification', lazy: lazyPage(() => import('@/pages/riders/RiderVerificationPage'), 'RiderVerificationPage') },
              { path: 'riders/payouts',      lazy: lazyPage(() => import('@/pages/riders/RiderPayoutsPage'), 'RiderPayoutsPage') },
              { path: 'riders/incentives',   lazy: lazyPage(() => import('@/pages/riders/IncentiveRulesPage'), 'IncentiveRulesPage') },
              { path: 'packages',       lazy: lazyPage(() => import('@/pages/packages/PackagesPage'), 'PackagesPage') },
              { path: 'coupons',        lazy: lazyPage(() => import('@/pages/coupons/CouponsPage'), 'CouponsPage') },
              { path: 'promotions',     lazy: lazyPage(() => import('@/pages/promotions/PromotionsPage'), 'PromotionsPage') },
              { path: 'subscriptions',  lazy: lazyPage(() => import('@/pages/subscriptions/SubscriptionsPage'), 'SubscriptionsPage') },
              { path: 'cashbook',       lazy: lazyPage(() => import('@/pages/finance/CashBookPage'), 'CashBookPage') },
              { path: 'expenses',       lazy: lazyPage(() => import('@/pages/finance/ExpensesPage'), 'ExpensesPage') },
              { path: 'royalty',        lazy: lazyPage(() => import('@/pages/finance/RoyaltyPage'), 'RoyaltyPage') },
              { path: 'platform-plans', lazy: lazyPage(() => import('@/pages/finance/PlatformPlansPage'), 'PlatformPlansPage') },
              { path: 'platform-billing', lazy: lazyPage(() => import('@/pages/finance/PlatformBillingPage'), 'PlatformBillingPage') },
            ],
          },
          // Unknown route → home (outside the gate so it never 403s).
          { path: '*', element: <Navigate to="/" replace /> },
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
      <StepUpDialog />
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  )
}
