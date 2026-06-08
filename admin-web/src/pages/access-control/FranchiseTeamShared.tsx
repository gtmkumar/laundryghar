import { cn } from '@/lib/utils'
import type { TeamRow } from '@/api/franchiseTeam'

const AVATAR_BG = [
  'bg-violet-500', 'bg-emerald-500', 'bg-sky-500', 'bg-amber-500',
  'bg-rose-500', 'bg-teal-500', 'bg-indigo-500', 'bg-orange-500',
]

function avatarColor(s: string): string {
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0
  return AVATAR_BG[h % AVATAR_BG.length]
}

export function statusTone(status: string): { dot: string; text: string } {
  const s = status.toLowerCase()
  if (['active', 'approved', 'verified', 'online'].includes(s)) return { dot: 'bg-emerald-500', text: 'text-emerald-700' }
  if (['invited', 'pending', 'onboarding', 'in_progress', 'setup', 'draft'].includes(s)) return { dot: 'bg-amber-500', text: 'text-amber-700' }
  if (['suspended', 'rejected', 'inactive', 'blocked'].includes(s)) return { dot: 'bg-red-500', text: 'text-red-600' }
  return { dot: 'bg-gray-300', text: 'text-gray-500' }
}

/** One normalised team row (used by both the hovercard preview and the drawer). */
export function TeamRowItem({ row, icon }: { row: TeamRow; icon?: React.ReactNode }) {
  const tone = statusTone(row.status)
  return (
    <div className="flex items-center gap-2.5 py-1.5">
      <span
        className={cn(
          'flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-[10px] font-semibold',
          row.initials ? cn('text-white', avatarColor(row.title)) : 'bg-gray-100 text-gray-400',
        )}
      >
        {row.initials ?? icon}
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium text-gray-800">{row.title}</p>
        {row.subtitle && <p className="truncate text-xs text-gray-400">{row.subtitle}</p>}
      </div>
      <span className="inline-flex shrink-0 items-center gap-1.5 text-xs font-medium capitalize">
        <span className={cn('h-1.5 w-1.5 rounded-full', tone.dot)} />
        <span className={tone.text}>{row.status}</span>
      </span>
    </div>
  )
}
