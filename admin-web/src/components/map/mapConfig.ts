/**
 * Map-provider abstraction.
 *
 * The live rider map renders through a provider so the choice of tiles is a
 * configuration concern, not a code change. Today only the free, key-less
 * **OpenStreetMap** (Leaflet) provider is wired. When a Google Maps key is added
 * later (planned: an Admin → Settings → Maps integration, mirroring Email & SMTP),
 * `useMapConfig` will resolve `{ provider: 'google', googleApiKey }` and the map
 * switcher will load Google instead — with zero changes to the Rider Ops page.
 *
 * Until that settings panel ships, an optional `.env` override lets you trial
 * Google locally: VITE_MAP_PROVIDER=google + VITE_GOOGLE_MAPS_API_KEY=...
 */
import { useSettings } from '@/hooks/useSettings'

export type MapProvider = 'osm' | 'google' | 'mapbox'

export interface MapConfig {
  provider: MapProvider
  googleApiKey: string | null
  mapboxToken: string | null
}

export function useMapConfig(): MapConfig {
  // Saved Admin → Settings → Maps config takes precedence; `.env` is a local
  // dev fallback so the provider can be trialled before the setting is saved.
  const { data } = useSettings()
  const saved = data?.maps

  const envProvider = (import.meta.env.VITE_MAP_PROVIDER as string | undefined)?.toLowerCase()
  const googleApiKey = saved?.googleApiKey || (import.meta.env.VITE_GOOGLE_MAPS_API_KEY as string | undefined) || null
  const mapboxToken  = saved?.mapboxToken  || (import.meta.env.VITE_MAPBOX_TOKEN as string | undefined) || null

  // A keyed provider is honoured only when its key is actually present; otherwise
  // we fall back to the free, key-less OSM tiles so the map is never blank.
  const wanted = (saved?.provider || envProvider || 'osm').toLowerCase()
  let provider: MapProvider = 'osm'
  if (wanted === 'google' && googleApiKey) provider = 'google'
  else if (wanted === 'mapbox' && mapboxToken) provider = 'mapbox'

  return { provider, googleApiKey, mapboxToken }
}

/** Map dot / marker colour per operational status (shared by map + legend). */
export const OPS_COLOR: Record<string, string> = {
  on_the_way: '#2563eb', // blue — en route
  arrived: '#16a34a',    // green — on site
  idle: '#d97706',       // amber — on duty, no active leg
  offline: '#9ca3af',    // grey — off duty
}

export const OPS_LABEL: Record<string, string> = {
  on_the_way: 'On the way',
  arrived: 'On site',
  idle: 'Idle (on duty)',
  offline: 'Offline',
}

/** Sensible default centre when no rider has a location yet (Gurugram, NCR). */
export const DEFAULT_CENTER: [number, number] = [28.4595, 77.0266]
export const DEFAULT_ZOOM = 11
