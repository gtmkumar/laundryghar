import { MapPin } from 'lucide-react'
import type { RiderLiveDto, RiderTrackPointDto } from '@/types/api'
import { useMapConfig, mapboxTiles } from './mapConfig'
import { RiderLeafletMap } from './RiderLeafletMap'
import { RiderGoogleMap } from './RiderGoogleMap'

interface Props {
  riders: RiderLiveDto[]
  selectedId: string | null
  trail: RiderTrackPointDto[]
  onSelect: (id: string) => void
}

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <div className="absolute right-3 top-3 z-10 flex items-center gap-1.5 rounded-lg bg-white/90 px-2.5 py-1 text-xs text-gray-500 shadow">
      <MapPin className="h-3 w-3" /> {children}
    </div>
  )
}

/**
 * Provider-agnostic live rider map. Resolves the configured provider and renders
 * the matching implementation — all three share the same rider/trail/selection
 * props. OSM/Leaflet is the key-less default; **Mapbox** renders its raster tiles
 * through the same Leaflet view; **Google** uses the Google Maps JS SDK. A keyed
 * provider only activates once its key/token is set (Settings → Maps), so this
 * falls back to OSM whenever a key is missing.
 */
export function RiderMap(props: Props) {
  const { provider, mapboxToken, googleApiKey } = useMapConfig()

  if (provider === 'google' && googleApiKey) {
    return <RiderGoogleMap {...props} apiKey={googleApiKey} />
  }

  if (provider === 'mapbox' && mapboxToken) {
    return (
      <div className="relative z-0 h-full w-full isolate">
        <RiderLeafletMap {...props} tiles={mapboxTiles(mapboxToken)} />
        <Badge>Mapbox</Badge>
      </div>
    )
  }

  return <RiderLeafletMap {...props} />
}
