import { identityClient, unwrap } from './client'
import type { ApiResponse, Navigator } from '@/types/api'

/** The signed-in user's sidebar menu, gated by their permissions (data-driven). */
export async function getNavigator(): Promise<Navigator> {
  const { data } = await identityClient.get<ApiResponse<Navigator>>('/api/v1/admin/navigator')
  return unwrap(data)
}
