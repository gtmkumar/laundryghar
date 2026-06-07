import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'
import { useOnboardingUi } from '@/stores/onboardingStore'
import { OnboardingContent } from '@/pages/access-control/onboarding/OnboardingDrawer'

/**
 * Workspace-mode panel: an in-flow flex sibling (not an overlay) that animates
 * its width open/closed, pushing the main content left instead of covering it.
 * The inner content keeps a fixed width so it doesn't reflow mid-animation.
 */
export function OnboardingPanel() {
  const { open, franchiseId, sessionKey, closeOnboarding } = useOnboardingUi()
  const [mounted, setMounted] = useState(open)
  const [expanded, setExpanded] = useState(false)

  useEffect(() => {
    if (open) {
      setMounted(true)
      const t = requestAnimationFrame(() => setExpanded(true))
      return () => cancelAnimationFrame(t)
    }
    setExpanded(false)
    const t = setTimeout(() => setMounted(false), 300)
    return () => clearTimeout(t)
  }, [open])

  if (!mounted) return null

  return (
    <div
      className={cn(
        'h-full shrink-0 overflow-hidden border-l border-gray-200 bg-white shadow-[-8px_0_24px_rgba(16,16,16,0.06)] transition-[width] duration-300 ease-out',
        expanded ? 'w-[36rem] max-w-[90vw]' : 'w-0',
      )}
    >
      <div className="h-full w-[36rem] max-w-[90vw]">
        <OnboardingContent key={sessionKey} franchiseId={franchiseId} onClose={closeOnboarding} />
      </div>
    </div>
  )
}
