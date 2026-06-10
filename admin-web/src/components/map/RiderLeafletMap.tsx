import { useEffect, useMemo } from 'react'
import { MapContainer, TileLayer, CircleMarker, Polyline, Tooltip, useMap } from 'react-leaflet'
import type { LatLngBoundsExpression } from 'leaflet'
import 'leaflet/dist/leaflet.css'
import type { RiderLiveDto, RiderTrackPointDto } from '@/types/api'
import { OPS_COLOR, DEFAULT_CENTER, DEFAULT_ZOOM, OSM_TILES, type MapTiles } from './mapConfig'

interface Props {
  riders: RiderLiveDto[]
  selectedId: string | null
  trail: RiderTrackPointDto[]
  onSelect: (id: string) => void
  /** Tile source — defaults to OSM. Pass Mapbox tiles for the Mapbox provider. */
  tiles?: MapTiles
}

/** Imperatively fit the map to the riders/trail once we have coordinates. */
function FitToData({ riders, trail, selectedId }: {
  riders: RiderLiveDto[]; trail: RiderTrackPointDto[]; selectedId: string | null
}) {
  const map = useMap()

  // When a rider is selected, pan/zoom to their trail (or marker).
  useEffect(() => {
    if (!selectedId) return
    const sel = riders.find((r) => r.id === selectedId)
    const pts: [number, number][] = trail.map((p) => [p.lat, p.lng])
    if (pts.length >= 2) {
      map.fitBounds(pts as LatLngBoundsExpression, { padding: [40, 40], maxZoom: 16 })
    } else if (sel?.lat != null && sel.lng != null) {
      map.setView([sel.lat, sel.lng], 15)
    }
  }, [selectedId, trail, riders, map])

  // On first load (no selection), fit to all located riders.
  useEffect(() => {
    if (selectedId) return
    const pts = riders
      .filter((r) => r.lat != null && r.lng != null)
      .map((r) => [r.lat as number, r.lng as number] as [number, number])
    if (pts.length === 1) map.setView(pts[0], 14)
    else if (pts.length > 1) map.fitBounds(pts as LatLngBoundsExpression, { padding: [40, 40], maxZoom: 15 })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [riders.length])

  return null
}

export function RiderLeafletMap({ riders, selectedId, trail, onSelect, tiles = OSM_TILES }: Props) {
  const located = useMemo(() => riders.filter((r) => r.lat != null && r.lng != null), [riders])
  const trailLine = useMemo<[number, number][]>(() => trail.map((p) => [p.lat, p.lng]), [trail])

  return (
    <MapContainer
      center={DEFAULT_CENTER}
      zoom={DEFAULT_ZOOM}
      scrollWheelZoom
      className="h-full w-full rounded-2xl"
      // Leaflet needs an explicit size; the parent provides height.
      style={{ minHeight: 420 }}
    >
      <TileLayer
        // `key` forces a fresh tile layer when the provider/source changes.
        key={tiles.url}
        attribution={tiles.attribution}
        url={tiles.url}
        tileSize={tiles.tileSize ?? 256}
        zoomOffset={tiles.zoomOffset ?? 0}
      />

      {trailLine.length >= 2 && (
        <Polyline positions={trailLine} pathOptions={{ color: '#2563eb', weight: 3, opacity: 0.7 }} />
      )}

      {located.map((r) => {
        const selected = r.id === selectedId
        const color = OPS_COLOR[r.opsStatus] ?? OPS_COLOR.offline
        return (
          <CircleMarker
            key={r.id}
            center={[r.lat as number, r.lng as number]}
            radius={selected ? 11 : 8}
            pathOptions={{
              color: selected ? '#111827' : color,
              weight: selected ? 3 : 1.5,
              fillColor: color,
              fillOpacity: r.isStale ? 0.45 : 0.9,
            }}
            eventHandlers={{ click: () => onSelect(r.id) }}
          >
            <Tooltip direction="top" offset={[0, -8]}>
              <span className="text-xs font-medium">
                {r.riderName ?? r.riderCode}
                {r.activeOrderNumber ? ` · ${r.activeOrderNumber}` : ''}
              </span>
            </Tooltip>
          </CircleMarker>
        )
      })}

      <FitToData riders={riders} trail={trail} selectedId={selectedId} />
    </MapContainer>
  )
}
