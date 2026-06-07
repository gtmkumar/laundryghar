import {
  createBrowserRouter,
  RouterProvider,
  Navigate,
} from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'

import { AppShell } from '@/components/layout/AppShell'
import { ProtectedRoute } from '@/components/layout/ProtectedRoute'
import { LoginPage } from '@/pages/auth/LoginPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { TenancyPage } from '@/pages/tenancy/TenancyPage'
import { CatalogPage } from '@/pages/catalog/CatalogPage'
import { OrdersPage } from '@/pages/orders/OrdersPage'
import { CmsPage } from '@/pages/cms/CmsPage'
import { AnalyticsPage } from '@/pages/analytics/AnalyticsPage'
import { ComingSoonPage } from '@/pages/ComingSoonPage'

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
    element: <LoginPage />,
  },
  {
    element: <ProtectedRoute />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true,           element: <DashboardPage /> },
          { path: 'tenancy',       element: <TenancyPage /> },
          { path: 'catalog',       element: <CatalogPage /> },
          { path: 'orders',        element: <OrdersPage /> },
          { path: 'cms',           element: <CmsPage /> },
          { path: 'analytics',     element: <AnalyticsPage /> },
          // New sidebar routes that don't have pages yet — lightweight placeholders
          { path: 'customers',     element: <ComingSoonPage name="Customers" /> },
          { path: 'riders',        element: <ComingSoonPage name="Riders" /> },
          { path: 'packages',      element: <ComingSoonPage name="Packages" /> },
          { path: 'coupons',       element: <ComingSoonPage name="Coupons" /> },
          { path: 'cashbook',      element: <ComingSoonPage name="Cash Book" /> },
          { path: 'expenses',      element: <ComingSoonPage name="Expenses" /> },
          { path: '*',             element: <Navigate to="/" replace /> },
        ],
      },
    ],
  },
])

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  )
}
