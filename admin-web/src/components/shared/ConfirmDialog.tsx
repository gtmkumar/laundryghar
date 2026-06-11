import { useEffect, useRef, useState, type ReactNode } from 'react'
import { AlertTriangle, Loader2, X } from 'lucide-react'

export type ConfirmTone = 'danger' | 'warning' | 'default'

interface ConfirmDialogProps {
  open: boolean
  onCancel: () => void
  /**
   * Invoked when the user confirms. When `requireReason` (or `reasonOptional`)
   * is set, the captured reason string is passed through. May be async — the
   * dialog shows a spinner and disables its buttons while the promise settles,
   * then closes via the caller (the caller controls `open`).
   */
  onConfirm: (reason?: string) => void | Promise<void>

  title: ReactNode
  /** Body copy describing the consequence. Keep it specific (names, amounts). */
  description?: ReactNode

  confirmLabel?: string
  cancelLabel?: string
  /** danger → red confirm button; warning → amber; default → brand green. */
  tone?: ConfirmTone

  /** Show a required reason textarea; confirm stays disabled until non-empty. */
  requireReason?: boolean
  /** Show an optional reason textarea (no gating). */
  reasonOptional?: boolean
  reasonLabel?: string
  reasonPlaceholder?: string

  /** Drive a spinner + disable while the caller's mutation is in flight. */
  busy?: boolean
}

const TONE_BTN: Record<ConfirmTone, string> = {
  danger: 'bg-red-600 hover:bg-red-700 text-white',
  warning: 'bg-amber-500 hover:bg-amber-600 text-white',
  default: 'bg-lg-green hover:bg-[var(--lg-green-hover)] text-white',
}

const TONE_ICON: Record<ConfirmTone, string> = {
  danger: 'bg-red-50 text-red-600',
  warning: 'bg-amber-50 text-amber-600',
  default: 'bg-lg-green/10 text-lg-green',
}

/**
 * The one confirmation primitive for destructive / financial / irreversible
 * actions across admin-web. Replaces ad-hoc `window.confirm` / `window.prompt`.
 *
 * A11y (covers WEB-9 for this surface):
 *  - role="dialog" + aria-modal, labelled by the title.
 *  - Focus moves into the dialog on open and is trapped (Tab/Shift+Tab cycle).
 *  - Escape cancels; backdrop click cancels; body scroll is locked while open.
 *  - Focus is restored to the previously-focused element on close.
 *
 * Reason capture (optional): pass `requireReason` to force a non-empty reason
 * (e.g. KYC reject, dispute) or `reasonOptional` for a free-text note that is
 * forwarded to `onConfirm(reason)`.
 */
export function ConfirmDialog({
  open,
  onCancel,
  onConfirm,
  title,
  description,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  tone = 'danger',
  requireReason = false,
  reasonOptional = false,
  reasonLabel = 'Reason',
  reasonPlaceholder = '',
  busy = false,
}: ConfirmDialogProps) {
  const [reason, setReason] = useState('')
  const panelRef = useRef<HTMLDivElement>(null)
  const confirmRef = useRef<HTMLButtonElement>(null)
  const restoreFocusRef = useRef<HTMLElement | null>(null)
  const showReason = requireReason || reasonOptional

  // Reset the captured reason whenever the dialog (re)opens.
  useEffect(() => {
    if (open) setReason('')
  }, [open])

  // Focus management + scroll lock + Escape, active only while open.
  useEffect(() => {
    if (!open) return
    restoreFocusRef.current = document.activeElement as HTMLElement | null
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    // Focus the confirm button (or the panel) on the next frame.
    const id = requestAnimationFrame(() => {
      confirmRef.current?.focus() ?? panelRef.current?.focus()
    })

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        if (!busy) onCancel()
        return
      }
      if (e.key !== 'Tab') return
      const focusables = panelRef.current?.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      )
      if (!focusables || focusables.length === 0) return
      const list = Array.from(focusables).filter((el) => !el.hasAttribute('disabled'))
      const first = list[0]
      const last = list[list.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      cancelAnimationFrame(id)
      document.removeEventListener('keydown', onKeyDown)
      document.body.style.overflow = prevOverflow
      restoreFocusRef.current?.focus?.()
    }
  }, [open, busy, onCancel])

  if (!open) return null

  const confirmDisabled = busy || (requireReason && reason.trim().length === 0)

  return (
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-black/40 p-4"
      onClick={() => !busy && onCancel()}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-dialog-title"
        tabIndex={-1}
        className="w-full max-w-md rounded-2xl bg-white shadow-2xl outline-none"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3 px-6 pt-6">
          <span className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${TONE_ICON[tone]}`}>
            <AlertTriangle className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="confirm-dialog-title" className="text-lg font-bold text-gray-900">
              {title}
            </h2>
            {description && <p className="mt-1 text-sm text-gray-500">{description}</p>}
          </div>
          <button
            type="button"
            onClick={() => !busy && onCancel()}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
            aria-label="Close"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {showReason && (
          <div className="px-6 pt-4">
            <label className="block">
              <span className="mb-1 block text-xs font-medium text-gray-500">
                {reasonLabel}
                {requireReason && <span className="text-red-500"> *</span>}
                {reasonOptional && <span className="text-gray-400"> (optional)</span>}
              </span>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder={reasonPlaceholder}
                rows={3}
                className="w-full resize-none rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
              />
            </label>
          </div>
        )}

        <div className="flex justify-end gap-2 px-6 py-5">
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
          >
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            onClick={() => void onConfirm(showReason ? reason.trim() || undefined : undefined)}
            disabled={confirmDisabled}
            className={`inline-flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-semibold disabled:opacity-60 ${TONE_BTN[tone]}`}
          >
            {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}

/**
 * Ergonomic hook for the common case: a single confirm gate driven by local
 * state. Returns `confirm(opts)` to open + a `<dialogProps>` object to spread
 * onto <ConfirmDialog/>. The action runs when the user confirms; the dialog
 * tracks its own busy state for async actions.
 *
 * Usage:
 *   const gate = useConfirm()
 *   <button onClick={() => gate.confirm({ title: 'Delete?', onConfirm: () => del() })}>…
 *   <ConfirmDialog {...gate.dialogProps} />
 */
interface ConfirmOptions {
  title: ReactNode
  description?: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  tone?: ConfirmTone
  requireReason?: boolean
  reasonOptional?: boolean
  reasonLabel?: string
  reasonPlaceholder?: string
  onConfirm: (reason?: string) => void | Promise<void>
}

export function useConfirm() {
  const [opts, setOpts] = useState<ConfirmOptions | null>(null)
  const [busy, setBusy] = useState(false)

  const confirm = (o: ConfirmOptions) => setOpts(o)
  const close = () => {
    if (!busy) setOpts(null)
  }

  const handleConfirm = async (reason?: string) => {
    if (!opts) return
    try {
      setBusy(true)
      await opts.onConfirm(reason)
      setOpts(null)
    } finally {
      setBusy(false)
    }
  }

  return {
    confirm,
    dialogProps: {
      open: opts !== null,
      busy,
      onCancel: close,
      onConfirm: handleConfirm,
      title: opts?.title ?? '',
      description: opts?.description,
      confirmLabel: opts?.confirmLabel,
      cancelLabel: opts?.cancelLabel,
      tone: opts?.tone,
      requireReason: opts?.requireReason,
      reasonOptional: opts?.reasonOptional,
      reasonLabel: opts?.reasonLabel,
      reasonPlaceholder: opts?.reasonPlaceholder,
    } satisfies ConfirmDialogProps,
  }
}
