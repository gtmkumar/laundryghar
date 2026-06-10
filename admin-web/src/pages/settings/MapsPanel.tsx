import { useEffect, useState } from 'react'
import { Loader2, Save, Map as MapIcon, Eye, EyeOff, CheckCircle2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdateMapsSettings } from '@/hooks/useSettings'
import type { AdminSettings, MapProviderId, UpdateMapsPayload } from '@/types/api'

const PROVIDERS: { id: MapProviderId; name: string; blurb: string; needsKey: boolean }[] = [
  { id: 'osm', name: 'OpenStreetMap', blurb: 'Free, no key required. Default.', needsKey: false },
  { id: 'google', name: 'Google Maps', blurb: 'Needs a Google Maps JavaScript API key.', needsKey: true },
  { id: 'mapbox', name: 'Mapbox', blurb: 'Needs a Mapbox public access token.', needsKey: true },
]

export function MapsPanel({ settings }: { settings: AdminSettings }) {
  const m = settings.maps
  const update = useUpdateMapsSettings()

  const [provider, setProvider] = useState<MapProviderId>(m.provider)
  const [googleApiKey, setGoogleApiKey] = useState('')
  const [mapboxToken, setMapboxToken] = useState('')
  const [showKeys, setShowKeys] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setProvider(m.provider)
    setGoogleApiKey('')
    setMapboxToken('')
  }, [m.provider])

  const googleSet = !!m.googleApiKey
  const mapboxSet = !!m.mapboxToken

  const save = async () => {
    setError(null)
    setSavedAt(null)
    const payload: UpdateMapsPayload = {
      provider,
      // Blank = keep the stored key (it's never wiped by an empty field).
      googleApiKey: googleApiKey.trim() || undefined,
      mapboxToken: mapboxToken.trim() || undefined,
    }
    try {
      await update.mutateAsync(payload)
      setGoogleApiKey('')
      setMapboxToken('')
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save map settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <MapIcon className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-gray-900">Maps</h2>
          <p className="text-sm text-gray-500">
            Tile provider for the live rider-tracking map. OpenStreetMap works out of the box;
            Google or Mapbox can be enabled by selecting it and supplying a key.
          </p>
        </div>
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        {/* Provider choice */}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          {PROVIDERS.map((p) => {
            const on = provider === p.id
            const configured = p.id === 'osm' || (p.id === 'google' ? googleSet : mapboxSet)
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => setProvider(p.id)}
                className={cn(
                  'rounded-xl border p-3 text-left transition-colors',
                  on ? 'border-lg-green bg-lg-green/5 ring-1 ring-lg-green/30' : 'border-gray-200 hover:bg-gray-50',
                )}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-semibold text-gray-900">{p.name}</span>
                  {on && <CheckCircle2 className="h-4 w-4 text-lg-green" />}
                </div>
                <p className="mt-1 text-xs text-gray-500">{p.blurb}</p>
                {p.needsKey && (
                  <span className={cn('mt-2 inline-block rounded-full px-2 py-0.5 text-[10px] font-medium',
                    configured ? 'bg-emerald-100 text-emerald-700' : 'bg-amber-100 text-amber-700')}>
                    {configured ? 'Key set' : 'Key needed'}
                  </span>
                )}
              </button>
            )
          })}
        </div>

        {/* Key fields — only for the providers that need one */}
        {provider !== 'osm' && (
          <div className="space-y-3 rounded-xl bg-gray-50 p-4">
            <div className="flex items-center justify-between">
              <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
                {provider === 'google' ? 'Google Maps API key' : 'Mapbox access token'}
              </p>
              <button type="button" onClick={() => setShowKeys((s) => !s)} className="text-gray-400 hover:text-gray-600">
                {showKeys ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
            {provider === 'google' ? (
              <input
                type={showKeys ? 'text' : 'password'}
                value={googleApiKey}
                onChange={(e) => setGoogleApiKey(e.target.value)}
                placeholder={googleSet ? '•••••••• (unchanged)' : 'AIza…'}
                className={inputCls}
                autoComplete="off"
              />
            ) : (
              <input
                type={showKeys ? 'text' : 'password'}
                value={mapboxToken}
                onChange={(e) => setMapboxToken(e.target.value)}
                placeholder={mapboxSet ? '•••••••• (unchanged)' : 'pk.…'}
                className={inputCls}
                autoComplete="off"
              />
            )}
            <p className="text-xs text-gray-400">
              Leave blank to keep the stored key. Map SDK keys are used in the browser — restrict the
              key to your admin domain for safety.
            </p>
          </div>
        )}

        <div className="flex items-center gap-3 pt-1">
          <button
            type="button"
            onClick={save}
            disabled={update.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save changes
          </button>
          {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
          {error && <span className="text-xs text-red-600">{error}</span>}
        </div>
      </div>

      <p className="text-xs text-gray-400">
        The Rider Ops live map reads this setting and renders on the selected provider — OpenStreetMap,
        Google Maps, or Mapbox. A keyed provider activates as soon as its key/token is saved; without
        one, the board falls back to OpenStreetMap.
      </p>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 font-mono text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'
