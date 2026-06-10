import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Loader2, MoreHorizontal, MoreVertical } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * A kebab/row-action menu that renders its popover in a portal to `document.body`
 * with fixed positioning, so it is never clipped by a table card's
 * `overflow-hidden` (or any scrollable ancestor). After mount it measures the
 * popover and flips it ABOVE the trigger when there isn't enough room below —
 * fixing the "last/second-last row menu gets cut off" problem.
 *
 * Usage:
 *   <ActionMenu busy={mutating}>
 *     {(close) => (<>
 *       <ActionMenuItem icon={Eye} onClick={() => { close(); onView() }}>View</ActionMenuItem>
 *     </>)}
 *   </ActionMenu>
 */
export function ActionMenu({
  children,
  busy = false,
  label = 'Row actions',
  icon = 'horizontal',
  width = 192,
}: {
  /** Render-prop receiving a `close()` you can call from an item's onClick. */
  children: (close: () => void) => React.ReactNode
  busy?: boolean
  label?: string
  icon?: 'horizontal' | 'vertical'
  /** Popover width in px (default 192 = w-48). */
  width?: number
}) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const btnRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const triggerRect = useRef<DOMRect | null>(null)
  const Icon = icon === 'vertical' ? MoreVertical : MoreHorizontal

  const close = () => setOpen(false)

  const openMenu = () => {
    const btn = btnRef.current
    if (!btn) return
    const r = btn.getBoundingClientRect()
    triggerRect.current = r
    // Initial guess (below, right-aligned); refined in useLayoutEffect once measured.
    setPos({ top: r.bottom + 4, left: Math.max(8, r.right - width) })
    setOpen(true)
  }

  // Roving focus within the menu: collect the focusable menu items.
  const menuItems = () =>
    menuRef.current
      ? Array.from(menuRef.current.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])'))
      : []

  // Arrow-key / Home / End navigation for the open popover (WAI-ARIA menu pattern).
  const onMenuKeyDown = (e: React.KeyboardEvent) => {
    const items = menuItems()
    if (items.length === 0) return
    const idx = items.indexOf(document.activeElement as HTMLElement)
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      items[(idx + 1) % items.length]?.focus()
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      items[idx < 0 ? items.length - 1 : (idx - 1 + items.length) % items.length]?.focus()
    } else if (e.key === 'Home') {
      e.preventDefault()
      items[0]?.focus()
    } else if (e.key === 'End') {
      e.preventDefault()
      items[items.length - 1]?.focus()
    }
  }

  // Once the popover is mounted we know its real height — flip up / clamp so it
  // always fits in the viewport. Runs before paint, so there's no visible jump.
  useLayoutEffect(() => {
    if (!open || !menuRef.current || !triggerRect.current) return
    const m = menuRef.current.getBoundingClientRect()
    const r = triggerRect.current
    const vh = window.innerHeight
    const fitsBelow = r.bottom + 4 + m.height <= vh - 8
    const fitsAbove = r.top - 4 - m.height >= 8
    let top = r.bottom + 4
    if (!fitsBelow && fitsAbove) top = r.top - m.height - 4
    else if (!fitsBelow) top = Math.max(8, vh - 8 - m.height)
    setPos((p) => (p && Math.abs(p.top - top) > 0.5 ? { ...p, top } : p))
    // Move focus into the menu so arrow keys work and Escape returns focus sanely.
    menuItems()[0]?.focus()
  }, [open])

  // Close on outside click, scroll (fixed coords go stale), resize, or Escape.
  useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => {
      if (btnRef.current?.contains(e.target as Node)) return
      if (menuRef.current?.contains(e.target as Node)) return
      setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDoc)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className="inline-block text-left">
      <button
        ref={btnRef}
        type="button"
        onClick={() => (open ? setOpen(false) : openMenu())}
        disabled={busy}
        className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-gray-400 hover:bg-gray-100 hover:text-gray-700 disabled:opacity-50"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Icon className="h-4 w-4" />}
      </button>
      {open && pos && createPortal(
        <div
          ref={menuRef}
          role="menu"
          onKeyDown={onMenuKeyDown}
          style={{ position: 'fixed', top: pos.top, left: pos.left, width }}
          className="z-50 overflow-hidden rounded-xl border border-gray-200 bg-white py-1 text-left text-sm shadow-lg"
        >
          {children(close)}
        </div>,
        document.body,
      )}
    </div>
  )
}

/** A single action row inside an {@link ActionMenu}. */
export function ActionMenuItem({
  icon: Icon,
  onClick,
  children,
  danger,
  className,
}: {
  icon: React.ElementType
  onClick: () => void
  children: React.ReactNode
  danger?: boolean
  className?: string
}) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2.5 px-3 py-2 text-gray-700 hover:bg-gray-50',
        danger && 'text-red-600',
        className,
      )}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
