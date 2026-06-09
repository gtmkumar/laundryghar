import { MapPin } from 'lucide-react'
import type { RiderLiveDto, RiderTrackPointDto } from '@/types/api'
import { useMapConfig } from './mapConfig'
import { RiderLeafletMap } from './RiderLeafletMap'

interface Props {
  riders: RiderLiveDto[]
  selectedId: string | null
  trail: RiderTrackPointDto[]
  onSelect: (id: string) => void
}

/**
 * Provider-agnostic live rider map. Resolves the configured provider and renders
 * the matching implementation. OSM/Leaflet is the default (key-less); Google is a
 * planned drop-in once the Settings → Maps key is supplied (see mapConfig.ts).
 */
export function RiderMap(props: Props) {
  const { provider } = useMapConfig()

  // Google / Mapbox are configured via Settings → Maps and land as full tile
  // renderers in a follow-up; until then we draw on OSM so the board is never
  // blank and surface a small note that the chosen provider is pending.
  if (provider !== 'osm') {
    const label = provider === 'google' ? 'Google Maps' : 'Mapbox'
    return (
      <div className="relative h-full w-full">
        <RiderLeafletMap {...props} />
        <div className="absolute right-3 top-3 z-[500] flex items-center gap-1.5 rounded-lg bg-white/90 px-2.5 py-1 text-xs text-gray-500 shadow">
          <MapPin className="h-3 w-3" /> {label} configured — rendering on OSM (renderer pending)
        </div>
      </div>
    )
  }

  return <RiderLeafletMap {...props} />
}
