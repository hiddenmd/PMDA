import type { LayerSpecification, SourceSpecification, StyleSpecification } from 'maplibre-gl'
import type { MapSettings } from '../../../api/types'

/** HUD colour tokens read from CSS at runtime (see MapView). */
export interface StyleTokens {
  background: string
  muted: string
  border: string
  borderStrong: string
  foreground: string
  mutedForeground: string
  primary: string
}

const FONT = ['Noto Sans Regular']
const WATER = { dark: '#0c1626', light: '#cfe0f0' } as const

/**
 * The Tactical HUD base map: OpenMapTiles-schema vector data (OpenFreeMap online, or a PMTiles file offline), drawn
 * dark and quiet so the flight data reads first — cyan coastline, dim borders and roads, uppercase labels.
 */
export function tacticalStyle(theme: 'dark' | 'light', settings: MapSettings, t: StyleTokens): StyleSpecification {
  const background: LayerSpecification = { id: 'background', type: 'background', paint: { 'background-color': t.background } }
  const source = baseSource(settings)
  if (!source) return { version: 8, sources: {}, layers: [background] }

  const water = WATER[theme]
  const v = { source: 'base' }
  const line = (id: string, sourceLayer: string, color: string, width: unknown, extra: Partial<LayerSpecification> = {}): LayerSpecification =>
    ({ id, type: 'line', ...v, 'source-layer': sourceLayer, paint: { 'line-color': color, 'line-width': width }, ...extra }) as LayerSpecification
  const label = (id: string, filter: unknown, size: unknown, color: string, minzoom = 0): LayerSpecification =>
    ({
      id,
      type: 'symbol',
      ...v,
      'source-layer': 'place',
      minzoom,
      filter,
      layout: { 'text-field': ['upcase', ['coalesce', ['get', 'name:latin'], ['get', 'name']]], 'text-font': FONT, 'text-size': size, 'text-letter-spacing': 0.1 },
      paint: { 'text-color': color, 'text-halo-color': t.background, 'text-halo-width': 1.2 },
    }) as LayerSpecification

  const layers: LayerSpecification[] = [
    background,
    { id: 'water', type: 'fill', ...v, 'source-layer': 'water', paint: { 'fill-color': water } } as LayerSpecification,
    {
      id: 'landuse-residential',
      type: 'fill',
      ...v,
      'source-layer': 'landuse',
      minzoom: 8,
      filter: ['==', ['get', 'class'], 'residential'],
      paint: { 'fill-color': t.muted, 'fill-opacity': 0.8 },
    } as LayerSpecification,
    line('waterway', 'waterway', water, ['interpolate', ['linear'], ['zoom'], 8, 0.5, 14, 2], { minzoom: 8 }),
    line('boundary-state', 'boundary', t.border, 0.8, { minzoom: 6, filter: ['==', ['get', 'admin_level'], 4], paint: { 'line-color': t.border, 'line-width': 0.8, 'line-dasharray': [3, 2] } }),
    line('boundary-country', 'boundary', t.borderStrong, 1.2, {
      filter: ['all', ['==', ['get', 'admin_level'], 2], ['!=', ['get', 'maritime'], 1]],
      paint: { 'line-color': t.borderStrong, 'line-width': 1.2, 'line-dasharray': [4, 2] },
    }),
    line('road-minor', 'transportation', t.border, ['interpolate', ['linear'], ['zoom'], 12, 0.4, 16, 1.5], {
      minzoom: 12,
      filter: ['match', ['get', 'class'], ['minor', 'tertiary', 'secondary'], true, false],
    }),
    line('road-primary', 'transportation', t.border, ['interpolate', ['linear'], ['zoom'], 9, 0.5, 14, 2], { minzoom: 9, filter: ['==', ['get', 'class'], 'primary'] }),
    line('road-motorway', 'transportation', t.borderStrong, ['interpolate', ['linear'], ['zoom'], 7, 0.6, 14, 2.5], {
      minzoom: 7,
      filter: ['match', ['get', 'class'], ['motorway', 'trunk'], true, false],
    }),
    // Water polygons outlined: the coastline in the HUD's primary colour.
    { id: 'coastline', type: 'line', ...v, 'source-layer': 'water', paint: { 'line-color': t.primary, 'line-opacity': 0.45, 'line-width': 1 } } as LayerSpecification,
  ]

  if (settings.glyphsUrl) {
    layers.push(
      label('place-country', ['==', ['get', 'class'], 'country'], 12, t.mutedForeground),
      label('place-city', ['==', ['get', 'class'], 'city'], ['interpolate', ['linear'], ['zoom'], 5, 10, 10, 13], t.mutedForeground, 5),
      label('place-town', ['==', ['get', 'class'], 'town'], 10, t.mutedForeground, 8),
      {
        id: 'water-name',
        type: 'symbol',
        ...v,
        'source-layer': 'water_name',
        layout: { 'text-field': ['upcase', ['get', 'name']], 'text-font': FONT, 'text-size': 10, 'text-letter-spacing': 0.2 },
        paint: { 'text-color': t.primary, 'text-opacity': 0.5, 'text-halo-color': water, 'text-halo-width': 1 },
      } as LayerSpecification,
    )
  }

  return { version: 8, ...(settings.glyphsUrl ? { glyphs: settings.glyphsUrl } : {}), sources: { base: source }, layers }
}

function baseSource(s: MapSettings): SourceSpecification | null {
  if (s.source === 'OpenFreeMap' && s.tileJsonUrl) return { type: 'vector', url: s.tileJsonUrl, attribution: s.attribution }
  if (s.source === 'PMTiles' && s.pmtilesUrl) return { type: 'vector', url: `pmtiles://${location.origin}${s.pmtilesUrl}`, attribution: s.attribution }
  return null
}
