import { AlertTriangle } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface ErrorStateProps {
  message?: string
  onRetry?: () => void
}

export function ErrorState({
  message = 'Something went wrong.',
  onRetry,
}: ErrorStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-16 gap-4">
      <AlertTriangle className="h-10 w-10 text-red-400" />
      <p className="text-gray-600 text-sm text-center max-w-xs">{message}</p>
      {onRetry && (
        <Button variant="outline" size="lg" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  )
}
