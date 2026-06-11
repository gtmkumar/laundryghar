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
          { path: 'new-order', lazy: lazyPage(() => import('@/pages/pos/NewOrderPage'), 'NewOrderPage') },
          { path: 'orders', lazy: lazyPage(() => import('@/pages/orders/OrdersPage'), 'OrdersPage') },
          { path: 'orders/:id', lazy: lazyPage(() => import('@/pages/orders/OrderDetailPage'), 'OrderDetailPage') },
          { path: 'cash-book', lazy: lazyPage(() => import('@/pages/cashbook/CashBookPage'), 'CashBookPage') },
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
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  )
}
