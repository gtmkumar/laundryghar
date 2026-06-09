import { useMemo, useState, type ReactNode } from 'react'
import { Search } from 'lucide-react'
import { DataTable, type Column, type SortState } from './DataTable'

/** A dropdown filter: keeps rows whose `value(row)` equals the chosen option. */
export interface FilterDef<T> {
  /** Stable key (used for the select's React key only). */
  key: string
  /** Placeholder shown for the "no filter" option, e.g. "All statuses". */
  allLabel: string
  /** Reads the row's value to compare against the selected option. */
  value: (row: T) => string
  options: { value: string; label: string }[]
}

interface FilterableTableProps<T> {
  columns: Column<T>[]
  /** The full, already-loaded row set. Filtering/searching/sorting is in-memory. */
  data: T[]
  keyFn: (row: T) => string
  onRowClick?: (row: T) => void

  /** Free-text search. `accessor` returns the haystack string for a row. */
  searchPlaceholder?: string
  searchAccessor?: (row: T) => string

  /** Zero or more dropdown filters rendered in the toolbar. */
  filters?: FilterDef<T>[]

  initialSort?: SortState

  /** Singular noun for the count line, e.g. "store" → "3 of 29 stores". */
  unit?: string
  /** Server-side grand total, when the loaded set may be a subset. */
  totalCount?: number

  emptyMessage?: string
  /** Shown when filters/search hide every row (the dataset itself is non-empty). */
  noMatchMessage?: string

  /** Extra controls rendered at the right of the toolbar (optional). */
  toolbarExtra?: ReactNode
  /** Rendered under the table — e.g. an infinite-scroll sentinel + spinner. */
  footer?: ReactNode
}

/**
 * One table to rule them all: a presentational {@link DataTable} wrapped with a
 * search box, config-driven dropdown filters, header sorting, and a result
 * count. All list screens (Stores, Franchises, Warehouses, …) should compose
 * this instead of re-implementing toolbars per page.
 *
 * Sorting reads `Column.sortAccessor` when present (for derived columns), else
 * the raw field named by `Column.sortKey`.
 */
export function FilterableTable<T>({
  columns,
  data,
  keyFn,
  onRowClick,
  searchPlaceholder = 'Search…',
  searchAccessor,
  filters = [],
  initialSort,
  unit = 'row',
  totalCount,
  emptyMessage = 'No records found.',
  noMatchMessage = 'No records match your filters.',
  toolbarExtra,
  footer,
}: FilterableTableProps<T>) {
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Record<string, string>>({})
  const [sort, setSort] = useState<SortState | null>(initialSort ?? null)

  // sortKey → how to read a sortable value for that column.
  const sortReaders = useMemo(() => {
    const m = new Map<string, (row: T) => string | number>()
    for (const c of columns) {
      if (!c.sortKey) continue
      if (c.sortAccessor) m.set(c.sortKey, c.sortAccessor)
      else if (typeof c.accessor !== 'function') {
        const key = c.accessor
        m.set(c.sortKey, (row) => row[key] as unknown as string | number)
      }
    }
    return m
  }, [columns])

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase()
    const rows = data.filter((row) => {
      for (const f of filters) {
        const chosen = selected[f.key]
        if (chosen && f.value(row) !== chosen) return false
      }
      if (q && searchAccessor && !searchAccessor(row).toLowerCase().includes(q)) return false
      return true
    })
    if (!sort) return rows
    const reader = sortReaders.get(sort.key)
    if (!reader) return rows
    const dir = sort.dir === 'asc' ? 1 : -1
    return [...rows].sort((a, b) => {
      const av = reader(a)
      const bv = reader(b)
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir
      return String(av).localeCompare(String(bv), undefined, { numeric: true }) * dir
    })
  }, [data, filters, selected, search, searchAccessor, sort, sortReaders])

  const toggleSort = (key: string) =>
    setSort((s) =>
      s && s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' },
    )

  return (
    <div>
      {/* Toolbar */}
      {(searchAccessor || filters.length > 0 || toolbarExtra) && (
        <div className="flex flex-wrap items-center gap-2 px-4 pt-4">
          {searchAccessor && (
            <div className="relative min-w-[200px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={searchPlaceholder}
                className="w-full rounded-lg border border-gray-200 bg-white py-2 pl-9 pr-3 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
              />
            </div>
          )}
          {filters.map((f) => (
            <select
              key={f.key}
              value={selected[f.key] ?? ''}
              onChange={(e) => setSelected((s) => ({ ...s, [f.key]: e.target.value }))}
              className="rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm text-gray-700 outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
            >
              <option value="">{f.allLabel}</option>
              {f.options.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          ))}
          {toolbarExtra}
        </div>
      )}

      <p className="px-4 py-3 text-sm text-gray-500">
        {visible.length}
        {typeof totalCount === 'number' && visible.length !== totalCount ? ` of ${totalCount}` : ''}{' '}
        {unit}
        {visible.length === 1 ? '' : 's'}
      </p>

      <DataTable
        columns={columns}
        data={visible}
        keyFn={keyFn}
        onRowClick={onRowClick}
        sort={sort}
        onSort={toggleSort}
        emptyMessage={data.length === 0 ? emptyMessage : noMatchMessage}
      />

      {footer}
    </div>
  )
}
