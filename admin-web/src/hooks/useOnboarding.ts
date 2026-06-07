import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getOnboarding,
  startOnboarding,
  saveDetails,
  saveCommercials,
  inviteOwner,
  addStore,
  activateFranchise,
} from '@/api/onboarding'
import type {
  OnboardingState,
  StartOnboardingPayload,
  SaveDetailsPayload,
  SaveCommercialsPayload,
  InviteOwnerPayload,
  AddStorePayload,
} from '@/types/api'

export function useOnboardingState(id: string | null) {
  return useQuery({
    queryKey: ['onboarding', id],
    queryFn: () => getOnboarding(id as string),
    enabled: !!id,
  })
}

/** Shared success handler: cache the returned state and refresh the franchises list. */
function useOnSaved() {
  const qc = useQueryClient()
  return (state: OnboardingState) => {
    qc.setQueryData(['onboarding', state.id], state)
    qc.invalidateQueries({ queryKey: ['access', 'franchises'] })
  }
}

export function useStartOnboarding() {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: (payload: StartOnboardingPayload) => startOnboarding(payload),
    onSuccess: onSaved,
  })
}

export function useSaveDetails(id: string) {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: (payload: SaveDetailsPayload) => saveDetails(id, payload),
    onSuccess: onSaved,
  })
}

export function useSaveCommercials(id: string) {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: (payload: SaveCommercialsPayload) => saveCommercials(id, payload),
    onSuccess: onSaved,
  })
}

export function useInviteOwner(id: string) {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: (payload: InviteOwnerPayload) => inviteOwner(id, payload),
    onSuccess: onSaved,
  })
}

export function useAddStore(id: string) {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: (payload: AddStorePayload) => addStore(id, payload),
    onSuccess: onSaved,
  })
}

export function useActivateFranchise(id: string) {
  const onSaved = useOnSaved()
  return useMutation({
    mutationFn: () => activateFranchise(id),
    onSuccess: onSaved,
  })
}
