import { createContext, useContext, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { X, Loader2, AlertTriangle } from 'lucide-react'
import { apiErrorMessage, apiFieldErrors } from '@/lib/apiError'

/**
 * Per-field API validation errors, keyed by a *normalized* field name
 * (lowercase, alphanumerics only) so the backend's `Price` / `name_localized` /
 * `nameLocalized` all match a `<Field name="nameLocalized">`. Provided by
 * FormDrawer, consumed by Field.
 */
const DrawerFieldErrorsContext = createContext<Record<string, string[]>>({})

function normalizeFieldKey(key: string): string {
  return key.toLowerCase().replace(/[^a-z0-9]/g, '')
}

function normalizeFieldErrors(errors: Record<string, string[]>): Record<string, string[]> {
  const out: Record<string, string[]> = {}
  for (const [k, v] of Object.entries(errors)) out[normalizeFieldKey(k)] = v
  return out
}

const WIDTHS = {
  sm: 'max-w-md',
  md: 'max-w-lg',
  lg: 'max-w-xl',
} as const

/**
 * Module-level stack of open drawer ids, ordered by mount. Only the LAST entry
 * (the topmost layer) handles Escape, so closing a nested/elevated drawer with
 * Esc never also closes the drawer underneath it. ConfirmDialog renders at a
 * higher z-index (z-[70]) and owns its own Escape, so a confirm opened over a
 * drawer still takes precedence — its listener was added later and stops the key
 * before this one runs, and it lives outside this stack.
 */
const drawerStack: string[] = []
/** The body overflow captured when the FIRST drawer opened; restored when the last closes. */
let savedBodyOverflow = ''

interface FormDrawerProps {
  open: boolean
  onClose: () => void
  /** Big bold heading. Optional when a custom `header` is supplied. */
  title?: ReactNode
  /** Small muted label above the title, e.g. "Tenancy". */
  eyebrow?: ReactNode
  /** Lucide icon component shown in the header chip. */
  icon?: React.ElementType
  width?: keyof typeof WIDTHS
  children: ReactNode

  /** Replace the default icon+eyebrow+title block with fully custom header content. */
  header?: ReactNode
  /** Buttons rendered to the left of the close (✕) button, e.g. an Edit toggle. */
  headerAction?: ReactNode
  /** Extra content rendered below the title inside the header (e.g. sub-tabs). */
  headerExtra?: ReactNode
  /** Override the default body classes (e.g. for list-style drawers). */
  bodyClassName?: string
  /** Raise to z-[60] so this drawer layers above another open drawer. */
  elevated?: boolean

  /**
   * Error banner pinned above the footer (always visible — never below the
   * fold of a long scrolling form). Announced via role="alert".
   */
  error?: string | null
  /**
   * Per-field API validation errors (e.g. from `apiFieldErrors(e)`), rendered
   * inline under any `<Field name="…">` whose name matches the key
   * (case/format-insensitive). Unmatched keys still surface via the banner /
   * global mutation toast, so nothing is silently dropped.
   */
  fieldErrors?: Record<string, string[]> | null

  // ── Footer ──
  /**
   * Footer override. A node renders custom footer content (full layout control);
   * `null` hides the footer entirely. Omit for the standard Cancel + submit row.
   */
  footer?: ReactNode | null
  /**
   * When provided, renders the primary submit button. May return a promise:
   * if it REJECTS, FormDrawer itself parses the API error envelope and renders
   * the banner + inline field errors — so a drawer that forgets its own
   * try/catch still gives visible feedback instead of silently staying open.
   */
  onSubmit?: () => void | Promise<unknown>
  submitLabel?: string
  submittingLabel?: string
  submitIcon?: React.ElementType
  submitting?: boolean
  submitDisabled?: boolean
  /** Cancel button label. When there's no `onSubmit`/`footer`, this is the lone button. */
  cancelLabel?: string
}

/**
 * The one drawer chrome for add / edit / view screens: right-side slide-over with
 * an icon header, scrollable body, inline error banner, and a standard
 * Cancel + submit footer. Pass `children` for the body and either `onSubmit`
 * (standard footer) or `footer` (custom). Omit both for a read-only "view"
 * drawer — the footer collapses to a single Close button.
 *
 * Compose bodies with {@link DrawerSection}, {@link Field}, and {@link drawerInputCls}.
 */
export function FormDrawer({
  open,
  onClose,
  title,
  eyebrow,
  icon: Icon,
  width = 'md',
  children,
  header,
  headerAction,
  headerExtra,
  bodyClassName,
  elevated = false,
  error,
  fieldErrors,
  footer,
  onSubmit,
  submitLabel = 'Save',
  submittingLabel,
  submitIcon: SubmitIcon,
  submitting = false,
  submitDisabled = false,
  cancelLabel = 'Cancel',
}: FormDrawerProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const restoreFocusRef = useRef<HTMLElement | null>(null)
  const id = useId()

  // Error caught from a rejected onSubmit promise (drawers without their own
  // try/catch). Cleared on (re)open and on every new submit attempt.
  const [caughtError, setCaughtError] = useState<unknown>(null)
  // Render-time state adjustment (not an effect): reset the caught error the
  // moment the drawer re-opens, before children render with stale errors.
  const [prevOpen, setPrevOpen] = useState(open)
  if (open !== prevOpen) {
    setPrevOpen(open)
    if (open && caughtError != null) setCaughtError(null)
  }

  const handleSubmit = onSubmit
    ? () => {
        setCaughtError(null)
        try {
          const result = onSubmit()
          if (result && typeof (result as Promise<unknown>).then === 'function') {
            void (result as Promise<unknown>).catch((e: unknown) => setCaughtError(e))
          }
        } catch (e) {
          setCaughtError(e)
        }
      }
    : undefined

  // Banner text: explicit page-provided `error` wins; otherwise derive one
  // from a caught submit rejection (the API envelope's validator text).
  const bannerError = error ?? (caughtError != null ? apiErrorMessage(caughtError) : null)

  // Inline per-field errors: page-provided `fieldErrors` merged with any parsed
  // from a caught rejection, normalized so PascalCase/snake_case keys match.
  const resolvedFieldErrors = useMemo(() => {
    const merged = { ...apiFieldErrors(caughtError), ...(fieldErrors ?? {}) }
    return normalizeFieldErrors(merged)
  }, [caughtError, fieldErrors])

  // A11y while open: scroll-lock, focus-into-panel + trap, and Escape-to-close —
  // but only the topmost layer responds to Escape (drawerStack), so an elevated /
  // nested drawer closes alone. Mirrors ConfirmDialog's pattern.
  useEffect(() => {
    if (!open) return

    // Capture the original body overflow only for the first drawer in the stack,
    // so nested drawers don't each overwrite it with the already-locked 'hidden'.
    if (drawerStack.length === 0) savedBodyOverflow = document.body.style.overflow
    drawerStack.push(id)
    restoreFocusRef.current = document.activeElement as HTMLElement | null
    document.body.style.overflow = 'hidden'

    // Move focus into the panel (first focusable, else the panel itself).
    const raf = requestAnimationFrame(() => {
      const first = panelRef.current?.querySelector<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      )
      ;(first ?? panelRef.current)?.focus()
    })

    const onKeyDown = (e: KeyboardEvent) => {
      // Only the topmost drawer reacts.
      if (drawerStack[drawerStack.length - 1] !== id) return
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        onClose()
        return
      }
      if (e.key !== 'Tab') return
      const focusables = panelRef.current?.querySelectorAll<HTMLElement>(
        'button, [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      )
      if (!focusables || focusables.length === 0) return
      const list = Array.from(focusables).filter((el) => !el.hasAttribute('disabled'))
      if (list.length === 0) return
      const firstEl = list[0]
      const lastEl = list[list.length - 1]
      if (e.shiftKey && document.activeElement === firstEl) {
        e.preventDefault()
        lastEl.focus()
      } else if (!e.shiftKey && document.activeElement === lastEl) {
        e.preventDefault()
        firstEl.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      cancelAnimationFrame(raf)
      document.removeEventListener('keydown', onKeyDown)
      const idx = drawerStack.lastIndexOf(id)
      if (idx !== -1) drawerStack.splice(idx, 1)
      // Restore body scroll only when no drawer remains open.
      if (drawerStack.length === 0) document.body.style.overflow = savedBodyOverflow
      restoreFocusRef.current?.focus?.()
    }
  }, [open, id, onClose])

  if (!open) return null

  return (
    <div
      className={`fixed inset-0 ${elevated ? 'z-[60]' : 'z-50'} flex justify-end bg-black/30`}
      onClick={onClose}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
        className={`flex h-full w-full ${WIDTHS[width]} flex-col bg-white shadow-2xl outline-none`}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="border-b border-gray-100 px-6 py-5">
          <div className="flex items-start justify-between gap-3">
            {header ?? (
              <div className="flex min-w-0 items-center gap-2.5">
                {Icon && (
                  <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
                    <Icon className="h-4 w-4" />
                  </span>
                )}
                <div className="min-w-0">
                  {eyebrow && <p className="truncate text-xs font-medium text-gray-400">{eyebrow}</p>}
                  <h2 className="truncate text-xl font-bold text-gray-900">{title}</h2>
                </div>
              </div>
            )}
            <div className="flex shrink-0 items-center gap-1">
              {headerAction}
              <button
                type="button"
                onClick={onClose}
                className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
              >
                <X className="h-5 w-5" />
              </button>
            </div>
          </div>
          {headerExtra && <div className="mt-4">{headerExtra}</div>}
        </div>

        {/* Body */}
        <div className={bodyClassName ?? 'flex-1 space-y-6 overflow-y-auto px-6 py-5'}>
          <DrawerFieldErrorsContext.Provider value={resolvedFieldErrors}>
            {children}
          </DrawerFieldErrorsContext.Provider>
        </div>

        {/* Error banner — pinned above the footer (never hidden below the fold
            of a long scrolling form) and announced to screen readers. */}
        {bannerError && (
          <div className="px-6 pb-3 pt-3 border-t border-gray-100">
            <div
              role="alert"
              className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700"
            >
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{bannerError}</span>
            </div>
          </div>
        )}

        {/* Footer — `null` hides it; a node gets full layout control; otherwise
            the standard right-aligned Cancel + submit row. */}
        {footer === null ? null : footer ? (
          <div className="border-t border-gray-100 px-6 py-4">{footer}</div>
        ) : (
          <div className="flex justify-end gap-2 border-t border-gray-100 px-6 py-4">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
            >
              {onSubmit ? cancelLabel : 'Close'}
            </button>
            {handleSubmit && (
              <button
                type="button"
                onClick={handleSubmit}
                disabled={submitting || submitDisabled}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {submitting ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                ) : (
                  SubmitIcon && <SubmitIcon className="h-3.5 w-3.5" />
                )}
                {submitting ? submittingLabel ?? submitLabel : submitLabel}
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

// ── Body primitives ───────────────────────────────────────────────────────────

export const drawerInputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15 disabled:cursor-not-allowed disabled:bg-gray-50 disabled:text-gray-500'

export function DrawerSection({ title, children }: { title?: ReactNode; children: ReactNode }) {
  return (
    <section className="space-y-3">
      {title && <h3 className="text-sm font-semibold text-gray-900">{title}</h3>}
      {children}
    </section>
  )
}

export function Field({
  label,
  children,
  hint,
  name,
}: {
  label: ReactNode
  children: ReactNode
  hint?: ReactNode
  /**
   * API field name this input maps to (e.g. "nameLocalized"). When the
   * enclosing FormDrawer has per-field API errors whose key matches
   * (case/format-insensitive), they render inline beneath the input.
   */
  name?: string
}) {
  const fieldErrors = useContext(DrawerFieldErrorsContext)
  const messages = name ? fieldErrors[normalizeFieldKey(name)] : undefined
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
      {messages && messages.length > 0 && (
        <span role="alert" className="mt-1 block text-xs text-red-600">
          {messages.join(' ')}
        </span>
      )}
      {hint && <span className="mt-1 block text-xs text-gray-500">{hint}</span>}
    </label>
  )
}

// ── Read-only "view" primitives ────────────────────────────────────────────────

/**
 * A titled group of label/value rows for read-only detail drawers. Wrap {@link DetailRow}s.
 * Default: a bordered card (divided rows). `plain`: a borderless block with an uppercase
 * muted header and no `<dl>` wrapper — for drawers that mix read-only rows with edit forms.
 */
export function DetailSection({
  title,
  children,
  plain = false,
}: {
  title?: ReactNode
  children: ReactNode
  plain?: boolean
}) {
  if (plain) {
    return (
      <div>
        {title && <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-500">{title}</h3>}
        {children}
      </div>
    )
  }
  return (
    <section className="space-y-2">
      {title && <h3 className="text-sm font-semibold text-gray-900">{title}</h3>}
      <dl className="divide-y divide-gray-50 rounded-xl border border-gray-100">{children}</dl>
    </section>
  )
}

/**
 * One label → value row inside a {@link DetailSection}. Default: a justified card row.
 * With `icon`: an icon-led row (icon · label · value) for borderless/`plain` sections.
 */
export function DetailRow({
  label,
  value,
  icon,
}: {
  label: ReactNode
  value: ReactNode
  icon?: ReactNode
}) {
  if (icon) {
    return (
      <div className="flex items-center gap-2 text-sm">
        <span className="text-gray-300">{icon}</span>
        <span className="text-gray-500">{label}</span>
        <span className="ml-auto truncate font-medium text-gray-700">{value}</span>
      </div>
    )
  }
  return (
    <div className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
      <dt className="text-gray-500">{label}</dt>
      <dd className="text-right font-medium text-gray-900">{value}</dd>
    </div>
  )
}
