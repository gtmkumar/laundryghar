import { ArrowDown, ArrowUp, ChevronsUpDown } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface Column<T> {
  header: React.ReactNode
  accessor: keyof T | ((row: T) => React.ReactNode)
  className?: string
  /** When set (together with the table's `onSort`), the header becomes a sort toggle. */
  sortKey?: string
}

export type SortDir = 'asc' | 'desc'
export interface SortState {
  key: string
  dir: SortDir
}

interface DataTableProps<T> {
  columns: Column<T>[]
  data: T[]
  keyFn: (row: T) => string
  onRowClick?: (row: T) => void
  emptyMessage?: string
  /** Current sort, when the table is sortable. */
  sort?: SortState | null
  /** Called with a column's `sortKey` when its header is clicked. */
  onSort?: (key: string) => void
}

export function DataTable<T>({
  columns,
  data,
  keyFn,
  onRowClick,
  emptyMessage = 'No records found.',
  sort,
  onSort,
}: DataTableProps<T>) {
  return (
    <div className="overflow-x-auto rounded-lg border border-gray-200">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            {columns.map((col, i) => {
              const sortable = !!col.sortKey && !!onSort
              const active = sortable && sort?.key === col.sortKey
              return (
                <th
                  key={i}
                  scope="col"
                  className={cn(
                    'px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500',
                    col.className,
                  )}
                >
                  {sortable ? (
                    <button
                      type="button"
                      onClick={() => onSort!(col.sortKey!)}
                      className="group inline-flex items-center gap-1 uppercase tracking-wider hover:text-gray-700"
                    >
                      {col.header}
                      {active ? (
                        sort!.dir === 'asc' ? (
                          <ArrowUp className="h-3 w-3" />
                        ) : (
                          <ArrowDown className="h-3 w-3" />
                        )
                      ) : (
                        <ChevronsUpDown className="h-3 w-3 text-gray-300 group-hover:text-gray-400" />
                      )}
                    </button>
                  ) : (
                    col.header
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100 bg-white">
          {data.length === 0 ? (
            <tr>
              <td
                colSpan={columns.length}
                className="px-4 py-8 text-center text-sm text-gray-400"
              >
                {emptyMessage}
              </td>
            </tr>
          ) : (
            data.map((row) => (
              <tr
                key={keyFn(row)}
                onClick={() => onRowClick?.(row)}
                className={cn(
                  'transition-colors',
                  onRowClick && 'cursor-pointer hover:bg-gray-50',
                )}
              >
                {columns.map((col, i) => (
                  <td
                    key={i}
                    className={cn('px-4 py-3 text-sm text-gray-700', col.className)}
                  >
                    {typeof col.accessor === 'function'
                      ? col.accessor(row)
                      : (row[col.accessor] as React.ReactNode)}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  )
}
