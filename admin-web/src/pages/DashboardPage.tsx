import { Building2, Package, ShoppingCart, Users } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { PageHeader } from '@/components/shared/PageHeader'
import { useAuthStore } from '@/stores/authStore'

const statCards = [
  { label: 'Stores', icon: Building2, value: '—', description: 'Active locations', color: 'text-blue-600' },
  { label: 'Orders Today', icon: ShoppingCart, value: '—', description: 'Placed today', color: 'text-green-600' },
  { label: 'Catalog Items', icon: Package, value: '—', description: 'Active items', color: 'text-purple-600' },
  { label: 'Staff', icon: Users, value: '—', description: 'Active users', color: 'text-orange-600' },
]

export function DashboardPage() {
  const { user } = useAuthStore()

  return (
    <div>
      <PageHeader
        title="Dashboard"
        description={`Welcome back${user?.name ? `, ${user.name}` : ''}. Here's what's happening.`}
      />

      {/* Stat cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {statCards.map((stat) => (
          <Card key={stat.label}>
            <CardHeader className="pb-2">
              <div className="flex items-center justify-between">
                <CardTitle className="text-sm font-medium text-gray-500">{stat.label}</CardTitle>
                <stat.icon className={`h-4 w-4 ${stat.color}`} />
              </div>
            </CardHeader>
            <CardContent>
              <p className="text-2xl font-bold text-gray-900">{stat.value}</p>
              <p className="text-xs text-gray-400 mt-0.5">{stat.description}</p>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Quick links */}
      <Card>
        <CardHeader>
          <CardTitle>Quick navigation</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <a
              href="/tenancy"
              className="flex items-center gap-3 rounded-lg border border-gray-200 p-4 hover:bg-gray-50 transition-colors"
            >
              <Building2 className="h-5 w-5 text-blue-500 shrink-0" />
              <div>
                <p className="text-sm font-medium text-gray-800">Tenancy</p>
                <p className="text-xs text-gray-400">Manage stores &amp; franchises</p>
              </div>
            </a>
            <a
              href="/catalog"
              className="flex items-center gap-3 rounded-lg border border-gray-200 p-4 hover:bg-gray-50 transition-colors"
            >
              <Package className="h-5 w-5 text-purple-500 shrink-0" />
              <div>
                <p className="text-sm font-medium text-gray-800">Catalog &amp; Pricing</p>
                <p className="text-xs text-gray-400">Services, items, price lists</p>
              </div>
            </a>
            <a
              href="/orders"
              className="flex items-center gap-3 rounded-lg border border-gray-200 p-4 hover:bg-gray-50 transition-colors"
            >
              <ShoppingCart className="h-5 w-5 text-green-500 shrink-0" />
              <div>
                <p className="text-sm font-medium text-gray-800">Orders</p>
                <p className="text-xs text-gray-400">View &amp; manage orders</p>
              </div>
            </a>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
