import { MapPin } from 'lucide-react'
import type { RiderLiveDto, RiderTrackPointDto } from '@/types/api'
import { useMapConfig, mapboxTiles } from './mapConfig'
import { RiderLeafletMap } from './RiderLeafletMap'

interface Props {
  riders: RiderLiveDto[]
  selectedId: string | null
  trail: RiderTrackPointDto[]
  onSelect: (id: string) => void
}

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <div className="absolute right-3 top-3 z-[500] flex items-center gap-1.5 rounded-lg bg-white/90 px-2.5 py-1 text-xs text-gray-500 shadow">
      <MapPin className="h-3 w-3" /> {children}
    </div>
  )
}

/**
 * Provider-agnostic live rider map. Resolves the configured provider and renders
 * the matching tiles. OSM/Leaflet is the key-less default; **Mapbox** renders its
 * raster tiles through the same Leaflet view once a token is set. Google can't be
 * served via Leaflet (ToS) — it needs the Google Maps JS SDK, a separate keyed
 * renderer — so it gracefully draws on OSM with a note until that ships.
 */
export function RiderMap(props: Props) {
  const { provider, mapboxToken } = useMapConfig()

  if (provider === 'mapbox' && mapboxToken) {
    return (
      <div className="relative h-full w-full">
        <RiderLeafletMap {...props} tiles={mapboxTiles(mapboxToken)} />
        <Badge>Mapbox</Badge>
      </div>
    )
  }

  if (provider === 'google') {
    return (
      <div className="relative h-full w-full">
        <RiderLeafletMap {...props} />
        <Badge>Google Maps configured — rendering on OSM (Google SDK renderer pending)</Badge>
      </div>
    )
  }

  return <RiderLeafletMap {...props} />
}
