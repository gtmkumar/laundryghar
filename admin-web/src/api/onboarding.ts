import { identityClient, unwrap } from './client'
import type {
  ApiResponse,
  OnboardingState,
  StartOnboardingPayload,
  SaveDetailsPayload,
  SaveCommercialsPayload,
  InviteOwnerPayload,
  AddStorePayload,
} from '@/types/api'

const BASE = '/api/v1/admin/franchises'

export async function getOnboarding(id: string): Promise<OnboardingState> {
  const { data } = await identityClient.get<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding`)
  return unwrap(data)
}

export async function startOnboarding(payload: StartOnboardingPayload): Promise<OnboardingState> {
  const { data } = await identityClient.post<ApiResponse<OnboardingState>>(`${BASE}/onboarding/start`, payload)
  return unwrap(data)
}

export async function saveDetails(id: string, payload: SaveDetailsPayload): Promise<OnboardingState> {
  const { data } = await identityClient.put<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding/details`, payload)
  return unwrap(data)
}

export async function saveCommercials(id: string, payload: SaveCommercialsPayload): Promise<OnboardingState> {
  const { data } = await identityClient.put<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding/commercials`, payload)
  return unwrap(data)
}

export async function inviteOwner(id: string, payload: InviteOwnerPayload): Promise<OnboardingState> {
  const { data } = await identityClient.post<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding/owner`, payload)
  return unwrap(data)
}

export async function addStore(id: string, payload: AddStorePayload): Promise<OnboardingState> {
  const { data } = await identityClient.post<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding/store`, payload)
  return unwrap(data)
}

export async function activateFranchise(id: string): Promise<OnboardingState> {
  const { data } = await identityClient.post<ApiResponse<OnboardingState>>(`${BASE}/${id}/onboarding/activate`, {})
  return unwrap(data)
}
