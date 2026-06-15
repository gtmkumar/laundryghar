import { useState, type ReactNode } from 'react'
import type { ConfirmDialogProps, ConfirmTone } from './ConfirmDialog'

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
