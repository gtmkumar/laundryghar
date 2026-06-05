import { AlertCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface ErrorStateProps {
  error: Error | null
  onRetry?: () => void
}

export function ErrorState({ error, onRetry }: ErrorStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-16 text-gray-500">
      <AlertCircle className="h-8 w-8 text-red-400 mb-3" />
      <p className="text-sm font-medium text-gray-700 mb-1">Failed to load data</p>
      <p className="text-xs text-gray-400 mb-4 max-w-sm text-center">
        {error?.message ?? 'An unexpected error occurred.'}
      </p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  )
}
