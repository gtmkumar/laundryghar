import { identityClient, unwrap } from './client'
import type {
  ApiResponse,
  AdminSettings,
  EmailSettingsView,
  ProvisioningView,
  UpdateEmailPayload,
  TestEmailResult,
} from '@/types/api'

const BASE = '/api/v1/admin/settings'

export async function getSettings(): Promise<AdminSettings> {
  const { data } = await identityClient.get<ApiResponse<AdminSettings>>(`${BASE}/`)
  return unwrap(data)
}

export async function updateEmailSettings(payload: UpdateEmailPayload): Promise<EmailSettingsView> {
  const { data } = await identityClient.put<ApiResponse<EmailSettingsView>>(`${BASE}/email`, payload)
  return unwrap(data)
}

export async function sendTestEmail(to: string, settings?: UpdateEmailPayload): Promise<TestEmailResult> {
  const { data } = await identityClient.post<ApiResponse<TestEmailResult>>(`${BASE}/email/test`, { to, settings })
  return unwrap(data)
}

export async function updateProvisioning(mode: string): Promise<ProvisioningView> {
  const { data } = await identityClient.put<ApiResponse<ProvisioningView>>(`${BASE}/provisioning`, { mode })
  return unwrap(data)
}
