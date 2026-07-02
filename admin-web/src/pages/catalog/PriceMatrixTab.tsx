import { useState } from 'react'
import { Loader2, Info } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePricingMatrix } from '@/hooks/useCatalog'

export function PriceMatrixTab() {
  const [storeId, setStoreId] = useState<string | undefined>(undefined)
  const { data, isLoading } = usePricingMatrix(storeId)

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-gray-400"><Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…</div>
  }
  if (!data) return <p className="py-12 text-center text-sm text-gray-400">No pricing data.</p>

  const { fabrics, rows, stores, priceListName, scopeType } = data

  return (
    <div>
      {/* Store filter */}
      {stores.length > 0 && (
        <div className="mb-3 flex flex-wrap items-center gap-1.5">
          <span className="mr-1 text-xs text-gray-500">Viewing rates for</span>
          <button
            type="button"
            onClick={() => setStoreId(undefined)}
            className={cn('rounded-full px-2.5 py-1 text-xs font-medium', !storeId ? 'bg-lg-green/10 text-lg-green' : 'bg-gray-100 text-gray-600 hover:bg-gray-200')}
          >
            All stores
          </button>
          {stores.map((s) => (
            <button
              key={s.id}
              type="button"
              onClick={() => setStoreId(s.id)}
              className={cn('rounded-full px-2.5 py-1 text-xs font-medium', storeId === s.id ? 'bg-lg-green/10 text-lg-green' : 'bg-gray-100 text-gray-600 hover:bg-gray-200')}
            >
              {s.name}
            </button>
          ))}
        </div>
      )}

      {priceListName && (
        <p className="mb-3 text-xs text-gray-500">
          Effective list: <span className="font-medium text-gray-700">{priceListName}</span>
          {scopeType && <span className="ml-1 rounded-full bg-gray-100 px-1.5 py-0.5 capitalize">{scopeType}</span>}
        </p>
      )}

      {/*
        The matrix is a read-only projection of the effective (published) price
        list: rows come from that list's priced items, and fabric columns are
        computed from the base × each fabric multiplier. It carries no item /
        service identity, so cells can't be edited in place — base rates are
        edited on the Items page and store overrides through price lists.
      */}
      <div className="mb-3 flex items-start gap-2 rounded-lg border border-blue-100 bg-blue-50/60 px-3 py-2 text-xs text-blue-800">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <span>
          {storeId
            ? 'Store overrides are edited via price lists — this view is read-only.'
            : 'Base rates are edited on the Items page (Bulk edit). Fabric columns are computed from the base rate; store overrides are managed through price lists.'}
        </span>
      </div>

      {rows.length === 0 ? (
        <p className="py-12 text-center text-sm text-gray-400">No published prices for this scope yet.</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-gray-100">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-100 bg-gray-50/60 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-500">
                <th className="sticky left-0 z-10 bg-gray-50/60 px-4 py-2.5">Item</th>
                {fabrics.map((f) => (
                  <th key={f.code} className="px-4 py-2.5 text-right">
                    {f.name}
                    <span className="ml-1 font-normal normal-case text-gray-400">×{f.multiplier.toFixed(2)}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={`${r.label}-${i}`} className="border-b border-gray-50 last:border-0">
                  <th scope="row" className="sticky left-0 z-10 bg-white px-4 py-2.5 text-left font-medium text-gray-800">
                    {r.label}
                    <span className="ml-2 text-xs font-normal text-gray-400">base ₹{r.basePrice}</span>
                  </th>
                  {fabrics.map((f) => (
                    <td key={f.code} className="px-4 py-2.5 text-right tabular-nums text-gray-900">
                      ₹{Math.round(r.basePrice * f.multiplier)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
