/**
 * Garment tag sheet — one printable slip per garment unit for the wash floor.
 *
 * Quantity expansion: a piece-priced line of qty 3 yields 3 tags (1/3, 2/3,
 * 3/3). Weight-priced lines (unit = kg) get a single tag showing the weight,
 * since you can't tag a fraction of a kilo. Each tag prints on its own slip
 * (see `.print-tag` page-break rules in index.css).
 */
import { Barcode } from '@/components/shared/Barcode'
import type { OrderDto } from '@/types/api'

interface GarmentTagsProps {
  order: OrderDto
  storeCode?: string
}

interface Tag {
  key: string
  itemLabel: string
  serviceLabel: string
  index: number
  count: number
  code: string
}

function buildTags(order: OrderDto, storeCode?: string): Tag[] {
  const tags: Tag[] = []
  const sc = storeCode ?? '—'
  for (const item of order.items ?? []) {
    const isWeight = item.unitOfMeasure === 'kg'
    // Piece-priced: one tag per whole unit. Weight-priced: a single tag.
    const count = isWeight ? 1 : Math.max(1, Math.round(item.quantity))
    for (let i = 0; i < count; i++) {
      tags.push({
        key: `${item.id}-${i}`,
        itemLabel: item.itemNameSnapshot,
        serviceLabel: item.serviceNameSnapshot,
        index: i + 1,
        count: isWeight ? 1 : count,
        // Scannable-looking code: order + item + sequence.
        code: `${order.orderNumber}-${item.id.slice(0, 4).toUpperCase()}-${i + 1}`,
      })
    }
  }
  void sc
  return tags
}

export function GarmentTags({ order, storeCode }: GarmentTagsProps) {
  const tags = buildTags(order, storeCode)

  return (
    <div className="print-area">
      {tags.map((tag) => (
        <div
          key={tag.key}
          className="print-tag mx-auto w-[300px] border border-black p-3 mb-3 text-black"
        >
          <div className="flex items-baseline justify-between">
            <span className="text-sm font-bold">{order.orderNumber}</span>
            <span className="text-xs font-semibold">
              {tag.index}/{tag.count}
            </span>
          </div>
          <div className="mt-1">
            <p className="text-base font-bold leading-tight">{tag.itemLabel}</p>
            <p className="text-xs">{tag.serviceLabel}</p>
          </div>
          <div className="mt-2">
            <Barcode value={tag.code} height={36} />
            <p className="text-center text-[10px] mt-0.5 tracking-widest">{tag.code}</p>
          </div>
          <div className="mt-1 flex items-center justify-between text-[10px]">
            <span>Store: {storeCode ?? '—'}</span>
            {order.isExpress && <span className="font-bold">EXPRESS</span>}
          </div>
        </div>
      ))}
    </div>
  )
}
