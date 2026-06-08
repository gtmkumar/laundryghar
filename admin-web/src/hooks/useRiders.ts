import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getRiders,
  getRider,
  inviteRiderUser,
  createRiderProfile,
  updateRider,
  verifyRider,
  rejectRider,
} from '@/api/riders'
import type {
  InviteRiderUserPayload,
  CreateRiderProfilePayload,
  UpdateRiderPayload,
} from '@/types/api'
import { useEffectiveBrandId } from './useBrandContext'

const RIDERS_PAGE_SIZE = 20

export interface RidersOpts {
  search?: string
  kycStatus?: string
  status?: string
  franchiseId?: string
  sort?: string
}

export function useRidersInfinite(opts: RidersOpts = {}) {
  const brandId = useEffectiveBrandId()
  return useInfiniteQuery({
    queryKey: [
      'riders',
      'infinite',
      brandId,
      opts.search ?? '',
      opts.kycStatus ?? '',
      opts.status ?? '',
      opts.franchiseId ?? '',
      opts.sort ?? '',
    ],
    queryFn: ({ pageParam }) =>
      getRiders(pageParam, RIDERS_PAGE_SIZE, {
        search: opts.search,
        kycStatus: opts.kycStatus,
        status: opts.status,
        franchiseId: opts.franchiseId,
        sort: opts.sort,
      }),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) => (lastPage.hasNextPage ? allPages.length + 1 : undefined),
    enabled: !!brandId,
  })
}

export function useRider(id: string | null) {
  return useQuery({
    queryKey: ['riders', 'detail', id],
    queryFn: () => getRider(id as string),
    enabled: !!id,
  })
}

/** What the drawer hands to the mutation: step-1 invite fields + step-2 profile fields (sans userId). */
export interface OnboardRiderInput {
  invite: InviteRiderUserPayload
  profile: Omit<CreateRiderProfilePayload, 'userId'>
}

/**
 * Frontend-orchestrated two-step onboarding:
 *   1. Create the rider login (Identity rider-invite) → read the new userId.
 *   2. Create the rider profile (Logistics) with that userId.
 *
 * If step 2 fails after step 1 succeeded, we surface a clear, distinct error so
 * the operator knows the login exists but the profile does not (partial state),
 * rather than silently swallowing it.
 */
export function useOnboardRider() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ invite, profile }: OnboardRiderInput) => {
      const user = await inviteRiderUser(invite)
      try {
        return await createRiderProfile({ ...profile, userId: user.id })
      } catch (e) {
        const detail = e instanceof Error ? e.message : 'unknown error'
        throw new Error(
          `Rider login was created (${invite.email}), but the rider profile could not be saved: ${detail}. ` +
            'Retry from the Riders list using this existing account before creating a duplicate login.',
        )
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['riders'] })
    },
  })
}

export function useUpdateRider() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateRiderPayload }) =>
      updateRider(id, payload),
    onSuccess: (rider) => {
      qc.invalidateQueries({ queryKey: ['riders'] })
      qc.setQueryData(['riders', 'detail', rider.id], rider)
    },
  })
}

export function useVerifyRider() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => verifyRider(id),
    onSuccess: (rider) => {
      qc.invalidateQueries({ queryKey: ['riders'] })
      qc.setQueryData(['riders', 'detail', rider.id], rider)
    },
  })
}

export function useRejectRider() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason?: string }) => rejectRider(id, reason),
    onSuccess: (rider) => {
      qc.invalidateQueries({ queryKey: ['riders'] })
      qc.setQueryData(['riders', 'detail', rider.id], rider)
    },
  })
}
