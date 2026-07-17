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
import { RequirePermission } from '@/components/layout/RequirePermission'
import { Toaster } from '@/components/shared/Toaster'
import { usePermissions } from '@/hooks/usePermissions'

// R3-NAV-1: thin permission gates for the route tree. Each reads the JWT-derived
// predicate and renders the friendly 403 panel when the staff login lacks it.
// Order create/read accept either the legacy `orders.*` or incoming `pos.order.*`
// family (see usePermissions / R3-SEC-2).
function RequireOrderCreate() {
  const { canCreateOrder } = usePermissions()
  return <RequirePermission allowed={canCreateOrder} area="new orders" />
}
function RequireOrderRead() {
  const { canViewOrders } = usePermissions()
  return <RequirePermission allowed={canViewOrders} area="the orders list" />
}
function RequireCashbook() {
  const { canManageCashbook } = usePermissions()
  return <RequirePermission allowed={canManageCashbook} area="the cash book" />
}

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
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <Navigate to="/new-order" replace /> },
          {
            element: <RequireOrderCreate />,
            children: [
              { path: 'new-order', lazy: lazyPage(() => import('@/pages/pos/NewOrderPage'), 'NewOrderPage') },
            ],
          },
          {
            element: <RequireOrderRead />,
            children: [
              { path: 'orders', lazy: lazyPage(() => import('@/pages/orders/OrdersPage'), 'OrdersPage') },
              { path: 'orders/:id', lazy: lazyPage(() => import('@/pages/orders/OrderDetailPage'), 'OrderDetailPage') },
            ],
          },
          {
            element: <RequireCashbook />,
            children: [
              { path: 'cash-book', lazy: lazyPage(() => import('@/pages/cashbook/CashBookPage'), 'CashBookPage') },
            ],
          },
          { path: '*', element: <Navigate to="/new-order" replace /> },
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
