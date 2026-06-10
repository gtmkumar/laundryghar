import { useEffect, useRef, useState } from 'react'
import { CheckCircle, QrCode, XCircle } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  DrawerSection,
  FormDrawer,
  drawerInputCls,
  DetailSection,
  DetailRow,
} from '@/components/shared/FormDrawer'
import { useGarmentByTag, useCreateProcessLog } from '@/hooks/useWarehouse'
import type { GarmentJourneyDto } from '@/types/api'

// ── Stage-advance map: current stage → next stage + processCode ───────────────
const NEXT_STAGE: Record<string, { toStage: string; processCode: string; label: string }> = {
  received:   { toStage: 'sorting',  processCode: 'SORT',     label: 'Move to Sorting'  },
  sorting:    { toStage: 'washing',  processCode: 'WASH',     label: 'Move to Washing'  },
  washing:    { toStage: 'drying',   processCode: 'DRY',      label: 'Move to Drying'   },
  drying:     { toStage: 'ironing',  processCode: 'IRON',     label: 'Move to Ironing'  },
  ironing:    { toStage: 'qc',       processCode: 'QC',       label: 'Move to QC'       },
  qc:         { toStage: 'packing',  processCode: 'PACK',     label: 'Move to Packing'  },
  packing:    { toStage: 'dispatched', processCode: 'DISPATCH', label: 'Dispatch'        },
}

interface ScanEntry {
  tagCode: string
  ok: boolean
  message: string
  stage?: string
}

interface Props {
  open: boolean
  onClose: () => void
  /** Warehouse ID to attach to the process log (from board summary). */
  warehouseId: string | null
}

/**
 * Scanner-gun-friendly scan-in drawer.
 *
 * Flow:
 *  1. User types/scans a tag code; Enter key submits.
 *  2. Lookup by tag → show garment summary.
 *  3. User presses "Advance stage" → POST /process-logs with scan_in action + next stage.
 *  4. Input re-focuses immediately for next scan; rolling log of last 5 scans shown.
 */
export function ScanInDrawer({ open, onClose, warehouseId }: Props) {
  const [input, setInput]       = useState('')
  const [committed, setCommitted] = useState('')  // tag code after Enter is pressed
  const [log, setLog]           = useState<ScanEntry[]>([])
  const inputRef                = useRef<HTMLInputElement>(null)

  const garmentQuery   = useGarmentByTag(committed)
  const createLog      = useCreateProcessLog()

  // Auto-focus input whenever drawer opens or after each action.
  useEffect(() => {
    if (open) {
      setTimeout(() => inputRef.current?.focus(), 50)
    }
  }, [open])

  // Clear transient state when drawer closes.
  useEffect(() => {
    if (!open) {
      setInput('')
      setCommitted('')
      createLog.reset()
    }
  }, [open]) // eslint-disable-line react-hooks/exhaustive-deps

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') {
      e.preventDefault()
      const code = input.trim()
      if (!code) return
      setCommitted(code)
      setInput('')
    }
  }

  async function handleAdvanceStage(journey: GarmentJourneyDto) {
    const { garment } = journey
    const next        = NEXT_STAGE[garment.currentStage]

    if (!next) {
      setLog((prev) => [
        { tagCode: garment.tagCode, ok: false, message: `Stage '${garment.currentStage}' has no next step` },
        ...prev.slice(0, 4),
      ])
      setCommitted('')
      setTimeout(() => inputRef.current?.focus(), 50)
      return
    }

    if (!warehouseId) {
      setLog((prev) => [
        { tagCode: garment.tagCode, ok: false, message: 'Warehouse ID missing — board context required' },
        ...prev.slice(0, 4),
      ])
      setCommitted('')
      setTimeout(() => inputRef.current?.focus(), 50)
      return
    }

    try {
      await createLog.mutateAsync({
        garmentId:       garment.id,
        warehouseId:     warehouseId,
        batchId:         garment.currentBatchId,
        processId:       null,
        processCode:     next.processCode,
        action:          'scan_in',
        fromStage:       garment.currentStage,
        toStage:         next.toStage,
        performedByName: null,
      })

      setLog((prev) => [
        {
          tagCode:  garment.tagCode,
          ok:       true,
          message:  `${garment.tagCode} → ${next.toStage}`,
          stage:    next.toStage,
        },
        ...prev.slice(0, 4),
      ])
    } catch (err) {
      setLog((prev) => [
        {
          tagCode: garment.tagCode,
          ok:      false,
          message: (err as Error)?.message ?? 'Failed to advance stage',
        },
        ...prev.slice(0, 4),
      ])
    }

    setCommitted('')
    createLog.reset()
    setTimeout(() => inputRef.current?.focus(), 50)
  }

  const journey     = garmentQuery.data
  const garment     = journey?.garment
  const isLooking   = !!committed && garmentQuery.isFetching
  const notFound    = !!committed && !garmentQuery.isFetching && !garment && !garmentQuery.isError
                      || (committed && garmentQuery.isError)
  const nextStep    = garment ? NEXT_STAGE[garment.currentStage] : null

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      title="Scan In"
      eyebrow="Warehouse"
      icon={QrCode}
      width="md"
      footer={null}
    >
      {/* Tag input — autofocused, scanner-gun compatible */}
      <DrawerSection title="Scan tag code">
        <input
          ref={inputRef}
          type="text"
          placeholder="Scan or type tag code, then press Enter"
          className={drawerInputCls}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          autoComplete="off"
          autoCapitalize="off"
          spellCheck={false}
        />
        <p className="mt-1 text-xs text-gray-400">
          Press Enter to look up the garment. Autofocuses after each scan.
        </p>
      </DrawerSection>

      {/* Lookup result */}
      {isLooking && (
        <p className="text-sm text-gray-400">Looking up {committed}…</p>
      )}

      {(garmentQuery.isError || notFound) && committed && (
        <div className="flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          <XCircle className="h-4 w-4 shrink-0" />
          Tag <span className="font-mono font-semibold">{committed}</span> not found in this brand.
        </div>
      )}

      {garment && (
        <DrawerSection title="Garment">
          <DetailSection>
            <DetailRow label="Tag"          value={<span className="font-mono">{garment.tagCode}</span>} />
            <DetailRow label="Current stage" value={garment.currentStage} />
            <DetailRow label="Status"        value={garment.status} />
            <DetailRow label="Last scanned"
              value={garment.lastScannedAt
                ? new Date(garment.lastScannedAt).toLocaleString()
                : '—'} />
          </DetailSection>

          {nextStep ? (
            <button
              type="button"
              disabled={createLog.isPending}
              onClick={() => handleAdvanceStage(journey!)}
              className="mt-3 w-full rounded-lg bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              {createLog.isPending ? 'Advancing…' : nextStep.label}
            </button>
          ) : (
            <p className="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-700">
              Stage <strong>{garment.currentStage}</strong> is the last step — no further advance.
            </p>
          )}
        </DrawerSection>
      )}

      {/* Rolling scan log */}
      {log.length > 0 && (
        <DrawerSection title="Recent scans">
          <ul className="space-y-1.5">
            {log.map((entry, i) => (
              <li
                key={i}
                className={cn(
                  'flex items-center gap-2 rounded-lg px-3 py-2 text-sm',
                  entry.ok
                    ? 'border border-green-200 bg-green-50 text-green-800'
                    : 'border border-red-200 bg-red-50 text-red-700',
                )}
              >
                {entry.ok
                  ? <CheckCircle className="h-3.5 w-3.5 shrink-0" />
                  : <XCircle    className="h-3.5 w-3.5 shrink-0" />
                }
                <span className="font-mono text-[12px]">{entry.tagCode}</span>
                <span className="ml-auto text-[11px] text-opacity-70">{entry.message}</span>
              </li>
            ))}
          </ul>
        </DrawerSection>
      )}
    </FormDrawer>
  )
}
