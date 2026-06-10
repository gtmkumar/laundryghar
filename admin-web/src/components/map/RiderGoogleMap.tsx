/// <reference types="google.maps" />
import { useEffect } from 'react'
import { APIProvider, Map, useMap } from '@vis.gl/react-google-maps'
import type { RiderLiveDto, RiderTrackPointDto } from '@/types/api'
import { OPS_COLOR, DEFAULT_CENTER, DEFAULT_ZOOM } from './mapConfig'

interface Props {
  riders: RiderLiveDto[]
  selectedId: string | null
  trail: RiderTrackPointDto[]
  onSelect: (id: string) => void
  apiKey: string
}

/**
 * Imperative overlay: rider markers (coloured by ops status), the selected rider's
 * trail polyline, and fit-to-data — all driven off the live `google.maps.Map`
 * instance. (Legacy `Marker` is fine here: a plain coloured dot needs no Map ID,
 * unlike AdvancedMarker.)
 */
function GoogleLayer({ riders, selectedId, trail, onSelect }: Omit<Props, 'apiKey'>) {
  const map = useMap()

  // Markers — rebuilt when riders / selection change.
  useEffect(() => {
    if (!map) return
    const located = riders.filter((r) => r.lat != null && r.lng != null)
    const markers = located.map((r) => {
      const color = OPS_COLOR[r.opsStatus] ?? OPS_COLOR.offline
      const selected = r.id === selectedId
      const marker = new google.maps.Marker({
        position: { lat: r.lat as number, lng: r.lng as number },
        map,
        title: `${r.riderName ?? r.riderCode}${r.activeOrderNumber ? ` · ${r.activeOrderNumber}` : ''}`,
        zIndex: selected ? 1000 : 1,
        icon: {
          path: google.maps.SymbolPath.CIRCLE,
          scale: selected ? 9 : 7,
          fillColor: color,
          fillOpacity: r.isStale ? 0.45 : 0.9,
          strokeColor: selected ? '#111827' : color,
          strokeWeight: selected ? 3 : 1.5,
        },
      })
      marker.addListener('click', () => onSelect(r.id))
      return marker
    })
    return () => markers.forEach((m) => m.setMap(null))
  }, [map, riders, selectedId, onSelect])

  // Trail polyline for the selected rider.
  useEffect(() => {
    if (!map) return
    const path = trail.map((p) => ({ lat: p.lat, lng: p.lng }))
    if (path.length < 2) return
    const line = new google.maps.Polyline({
      path,
      map,
      strokeColor: '#2563eb',
      strokeOpacity: 0.7,
      strokeWeight: 3,
    })
    return () => line.setMap(null)
  }, [map, trail])

  // Fit to the trail (when a rider is selected) or to all located riders.
  useEffect(() => {
    if (!map) return
    if (selectedId) {
      const pts = trail.map((p) => ({ lat: p.lat, lng: p.lng }))
      if (pts.length >= 2) {
        const b = new google.maps.LatLngBounds()
        pts.forEach((p) => b.extend(p))
        map.fitBounds(b, 40)
      } else {
        const sel = riders.find((r) => r.id === selectedId)
        if (sel?.lat != null && sel.lng != null) {
          map.setCenter({ lat: sel.lat, lng: sel.lng })
          map.setZoom(15)
        }
      }
      return
    }
    const pts = riders
      .filter((r) => r.lat != null && r.lng != null)
      .map((r) => ({ lat: r.lat as number, lng: r.lng as number }))
    if (pts.length === 1) {
      map.setCenter(pts[0])
      map.setZoom(14)
    } else if (pts.length > 1) {
      const b = new google.maps.LatLngBounds()
      pts.forEach((p) => b.extend(p))
      map.fitBounds(b, 40)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, selectedId, trail, riders.length])

  return null
}

/** Google Maps live rider board. Mirrors RiderLeafletMap; selected via Settings → Maps. */
export function RiderGoogleMap({ apiKey, ...layer }: Props) {
  return (
    <APIProvider apiKey={apiKey}>
      <Map
        defaultCenter={{ lat: DEFAULT_CENTER[0], lng: DEFAULT_CENTER[1] }}
        defaultZoom={DEFAULT_ZOOM}
        gestureHandling="greedy"
        disableDefaultUI={false}
        clickableIcons={false}
        // `isolate relative z-0`: scope Google Maps' internal z-indexed panes so they
        // can't paint over right-side drawers.
        className="isolate relative z-0 h-full w-full overflow-hidden rounded-2xl"
        style={{ minHeight: 420 }}
      >
        <GoogleLayer {...layer} />
      </Map>
    </APIProvider>
  )
}
