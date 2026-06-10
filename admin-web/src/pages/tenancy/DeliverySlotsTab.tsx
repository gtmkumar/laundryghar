import { useMemo, useState } from 'react'
import { Pencil } from 'lucide-react'
import { useDeliverySlots } from '@/hooks/usePickups'
import { usePermissions } from '@/hooks/usePermissions'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { type Column } from '@/components/shared/DataTable'
import { FilterableTable, type FilterDef } from '@/components/shared/FilterableTable'
import { ActionMenu, ActionMenuItem } from '@/components/ui/ActionMenu'
import { Badge } from '@/components/ui/badge'
import { formatDate } from '@/lib/utils'
import type { DeliverySlotDto } from '@/types/api'
import {
  AddSlotDrawer,
  EditSlotDrawer,
  SLOT_TYPES,
  useSlotStores,
} from './DeliverySlotDrawers'

// Backend gate: POST/PUT delivery-slots → permission:delivery.slot.manage.
const PERM_MANAGE = 'delivery.slot.manage'

function timeShort(t: string): string {
  return t.length >= 5 ? t.slice(0, 5) : t
}

export function DeliverySlotsTab({
  addOpen,
  onAddClose,
}: {
  addOpen: boolean
  onAddClose: () => void
}) {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission(PERM_MANAGE)

  const { stores, storeName } = useSlotStores()

  const { data, isLoading, isError, error, refetch } = useDeliverySlots({ pageSize: 200 })
  const slots = useMemo(() => data?.list ?? [], [data])
  const total = data?.totalCount

  const [editing, setEditing] = useState<DeliverySlotDto | null>(null)

  const columns: Column<DeliverySlotDto>[] = [
    {
      header: 'Store',
      accessor: (r) =>
        storeName.has(r.storeId) ? (
          <span className="text-gray-700">{storeName.get(r.storeId)}</span>
        ) : (
          <span className="font-mono text-[11px] text-gray-400">{r.storeId.slice(0, 8)}…</span>
        ),
      sortKey: 'store',
      sortAccessor: (r) => storeName.get(r.storeId) ?? r.storeId,
    },
    {
      header: 'Date',
      accessor: (r) => <span className="whitespace-nowrap">{formatDate(r.slotDate)}</span>,
      sortKey: 'slotDate',
      sortAccessor: (r) => `${r.slotDate} ${r.slotStart}`,
    },
    {
      header: 'Window',
      accessor: (r) => (
        <span className="whitespace-nowrap tabular-nums text-gray-600">
          {timeShort(r.slotStart)} – {timeShort(r.slotEnd)}
        </span>
      ),
      sortKey: 'window',
      sortAccessor: (r) => r.slotStart,
    },
    {
      header: 'Type',
      accessor: (r) => <span className="capitalize text-gray-600">{r.slotType}</span>,
      sortKey: 'slotType',
      sortAccessor: (r) => r.slotType,
    },
    {
      header: 'Booked / Cap',
      accessor: (r) => (
        <span className="tabular-nums text-gray-700">
          {r.bookedCount} / {r.capacity}
          {r.available <= 0 && <span className="ml-1 text-xs text-amber-600">full</span>}
        </span>
      ),
      className: 'text-right',
      sortKey: 'capacity',
      sortAccessor: (r) => r.capacity,
    },
    {
      header: 'Express',
      accessor: (r) => (r.isExpress ? <Badge variant="warning">Express</Badge> : null),
    },
    {
      header: 'Status',
      accessor: (r) => (
        <Badge variant={r.isActive ? 'success' : 'secondary'} className="capitalize">
          {r.isActive ? 'active' : 'closed'}
        </Badge>
      ),
      sortKey: 'status',
      sortAccessor: (r) => (r.isActive ? 'active' : 'closed'),
    },
    ...(canManage
      ? [
          {
            header: '',
            className: 'w-12 text-right',
            accessor: (r: DeliverySlotDto) => (
              <div onClick={(e) => e.stopPropagation()}>
                <ActionMenu label="Slot actions">
                  {(close) => (
                    <ActionMenuItem icon={Pencil} onClick={() => { close(); setEditing(r) }}>
                      Edit
                    </ActionMenuItem>
                  )}
                </ActionMenu>
              </div>
            ),
          } as Column<DeliverySlotDto>,
        ]
      : []),
  ]

  const filters: FilterDef<DeliverySlotDto>[] = [
    {
      key: 'store',
      allLabel: 'All stores',
      value: (s) => s.storeId,
      options: stores.map((s) => ({ value: s.id, label: s.name })),
    },
    {
      key: 'type',
      allLabel: 'All types',
      value: (s) => s.slotType,
      options: SLOT_TYPES.map((t) => ({ value: t.value, label: t.label })),
    },
    {
      key: 'status',
      allLabel: 'All statuses',
      value: (s) => (s.isActive ? 'active' : 'closed'),
      options: [
        { value: 'active', label: 'Active' },
        { value: 'closed', label: 'Closed' },
      ],
    },
  ]

  if (isLoading) return <LoadingState message="Loading delivery slots..." />
  if (isError) return <ErrorState error={error as Error} onRetry={() => void refetch()} />

  return (
    <>
      <FilterableTable
        columns={columns}
        data={slots}
        keyFn={(r) => r.id}
        onRowClick={canManage ? (s) => setEditing(s) : undefined}
        unit="slot"
        totalCount={total}
        searchPlaceholder="Search store, date…"
        searchAccessor={(s) => `${storeName.get(s.storeId) ?? ''} ${s.slotDate} ${s.slotType}`}
        filters={filters}
        initialSort={{ key: 'slotDate', dir: 'asc' }}
        emptyMessage="No delivery slots yet. Add one to let customers book pickup or delivery windows."
        noMatchMessage="No slots match your filters."
      />

      <AddSlotDrawer open={addOpen} onClose={onAddClose} stores={stores} />
      <EditSlotDrawer
        slot={editing}
        onClose={() => setEditing(null)}
        storeName={editing ? storeName.get(editing.storeId) : undefined}
      />
    </>
  )
}
