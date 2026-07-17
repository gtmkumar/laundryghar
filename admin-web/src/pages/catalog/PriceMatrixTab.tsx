import { useState } from 'react'
import { Loader2, Info, Pencil } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePricingMatrix, useSaveItemPricing } from '@/hooks/useCatalog'
import { apiErrorMessage } from '@/lib/apiError'
import type { PricingMatrixRow } from '@/types/api'

/**
 * Base-rate cell of an editable matrix row. Editing writes the single service
 * price through SaveItemPricing (fabricTypeIds omitted, so the item's fabric
 * set is untouched); fabric columns stay computed from base × multiplier.
 */
function EditableBase({ row, onError }: { row: PricingMatrixRow; onError: (m: string | null) => void }) {
  const save = useSaveItemPricing()
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState('')

  const start = () => {
    setValue(String(row.basePrice))
    setEditing(true)
    onError(null)
  }

  const commit = async () => {
    const next = Number(value)
    setEditing(false)
    if (!Number.isFinite(next) || next < 0 || next === row.basePrice) return
    try {
      await save.mutateAsync({
        id: row.itemId,
        payload: { servicePrices: [{ serviceId: row.serviceId, basePrice: next }] },
      })
    } catch (e) {
      onError(apiErrorMessage(e, 'Could not save the price.'))
    }
  }

  if (editing) {
    return (
      <span className="ml-2 inline-flex items-center text-xs font-normal">
        <span className="mr-0.5 text-gray-400">₹</span>
        <input
          autoFocus
          type="number"
          min={0}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onBlur={() => void commit()}
          onKeyDown={(e) => {
            if (e.key === 'Enter') e.currentTarget.blur()
            if (e.key === 'Escape') setEditing(false)
          }}
          className="w-20 rounded border border-lg-green/50 px-1.5 py-0.5 text-right tabular-nums focus:outline-none focus:ring-1 focus:ring-lg-green"
        />
      </span>
    )
  }

  // The save is optimistic (the hook patches the cached matrix in onMutate),
  // so the new base renders immediately — no pending spinner or disable.
  return (
    <button
      type="button"
      onClick={start}
      title="Edit base rate"
      className="group ml-2 inline-flex items-center gap-1 rounded px-1 text-xs font-normal text-gray-400 hover:bg-lg-green/5 hover:text-lg-green"
    >
      base ₹{row.basePrice}
      <Pencil className="h-2.5 w-2.5 opacity-0 transition-opacity group-hover:opacity-100" />
    </button>
  )
}

export function PriceMatrixTab() {
  const [storeId, setStoreId] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const { data, isLoading } = usePricingMatrix(storeId)

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-gray-400"><Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…</div>
  }
  if (!data) return <p className="py-12 text-center text-sm text-gray-400">No pricing data.</p>

  const { fabrics, rows, stores, priceListName, scopeType, isWorkingList } = data

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

      <div className="mb-3 flex items-start gap-2 rounded-lg border border-blue-100 bg-blue-50/60 px-3 py-2 text-xs text-blue-800">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <span>
          {isWorkingList
            ? 'Click a base rate to edit it in place. Fabric columns are computed from the base rate; fabric-specific rows and store overrides are managed through price lists.'
            : 'This scope resolves to a published override list — edit its rates via the Price lists tab. Fabric columns are computed from the base rate.'}
        </span>
      </div>

      {error && (
        <div className="mb-3 rounded-lg border border-red-100 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</div>
      )}

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
                <tr key={`${r.itemId}-${r.serviceId}-${i}`} className="border-b border-gray-50 last:border-0">
                  <th scope="row" className="sticky left-0 z-10 bg-white px-4 py-2.5 text-left font-medium text-gray-800">
                    {r.label}
                    {r.editable ? (
                      <EditableBase row={r} onError={setError} />
                    ) : (
                      <span className="ml-2 text-xs font-normal text-gray-400">base ₹{r.basePrice}</span>
                    )}
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
