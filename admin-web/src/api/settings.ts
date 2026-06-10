import { identityClient, unwrap } from './client'
import type {
  ApiResponse,
  AdminSettings,
  EmailSettingsView,
  ProvisioningView,
  UpdateEmailPayload,
  TestEmailResult,
  MapsSettingsView,
  UpdateMapsPayload,
  PayoutSettingsView,
  UpdatePayoutPayload,
  PaymentGatewaySettingsView,
  UpdatePaymentGatewayPayload,
  WhatsAppSettingsView,
  UpdateWhatsAppPayload,
  SmsSettingsView,
  UpdateSmsPayload,
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

export async function updateMapsSettings(payload: UpdateMapsPayload): Promise<MapsSettingsView> {
  const { data } = await identityClient.put<ApiResponse<MapsSettingsView>>(`${BASE}/maps`, payload)
  return unwrap(data)
}

export async function updatePayoutSettings(payload: UpdatePayoutPayload): Promise<PayoutSettingsView> {
  const { data } = await identityClient.put<ApiResponse<PayoutSettingsView>>(`${BASE}/payout`, payload)
  return unwrap(data)
}

export async function updatePaymentGatewaySettings(
  payload: UpdatePaymentGatewayPayload,
): Promise<PaymentGatewaySettingsView> {
  const { data } = await identityClient.put<ApiResponse<PaymentGatewaySettingsView>>(
    `${BASE}/payment-gateway`,
    payload,
  )
  return unwrap(data)
}

export async function updateWhatsAppSettings(payload: UpdateWhatsAppPayload): Promise<WhatsAppSettingsView> {
  const { data } = await identityClient.put<ApiResponse<WhatsAppSettingsView>>(`${BASE}/whatsapp`, payload)
  return unwrap(data)
}

export async function updateSmsSettings(payload: UpdateSmsPayload): Promise<SmsSettingsView> {
  const { data } = await identityClient.put<ApiResponse<SmsSettingsView>>(`${BASE}/sms`, payload)
  return unwrap(data)
}
