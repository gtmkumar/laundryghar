import { useQuery } from '@tanstack/react-query'
import { getTerminology } from '@/api/terminology'
import { useActiveVertical } from './useActiveVertical'

const ONE_DAY = 24 * 60 * 60 * 1000

/**
 * The active brand's vocabulary, server-driven.
 *
 * Terminology changes only when the catalogue is re-seeded and is identical for every brand in a
 * vertical, so it is cached for a day. Callers should read through `lib/verticalTerms`, which falls
 * back to its built-in defaults while this is loading — a screen must never blank waiting for a word.
 */
export function useTerminology() {
  const verticalKey = useActiveVertical()
  return useQuery({
    queryKey: ['terminology', verticalKey ?? 'default'],
    queryFn: () => getTerminology(verticalKey),
    staleTime: ONE_DAY,
    gcTime: ONE_DAY,
  })
}
