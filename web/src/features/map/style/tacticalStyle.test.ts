import { validateStyleMin } from '@maplibre/maplibre-gl-style-spec'
import { describe, expect, it } from 'vitest'
import type { MapSettings } from '../../../api/types'
import { tacticalStyle, type StyleTokens } from './tacticalStyle'

const tokens: StyleTokens = {
  background: '#080b12',
  muted: '#0f1726',
  border: '#1a2436',
  borderStrong: '#22324d',
  foreground: '#cfd8e6',
  mutedForeground: '#8fa3bf',
  primary: '#4cc3ff',
}
const online: MapSettings = {
  source: 'OpenFreeMap',
  tileJsonUrl: 'https://tiles.openfreemap.org/planet',
  glyphsUrl: 'https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf',
  pmtilesUrl: null,
  terrainUrl: null,
  attribution: 'OSM',
  maxZoom: 15,
}

describe('tacticalStyle', () => {
  it.each(['dark', 'light'] as const)('is a valid MapLibre style (%s)', (theme) => {
    expect(validateStyleMin(tacticalStyle(theme, online, tokens))).toEqual([])
  })

  it('uses the OpenFreeMap TileJSON as the vector source and the configured glyphs', () => {
    const s = tacticalStyle('dark', online, tokens)
    expect(s.sources.base).toEqual({ type: 'vector', url: 'https://tiles.openfreemap.org/planet', attribution: 'OSM' })
    expect(s.glyphs).toBe(online.glyphsUrl)
  })

  it('uses a pmtiles:// source for the offline file', () => {
    const s = tacticalStyle('dark', { ...online, source: 'PMTiles', tileJsonUrl: null, pmtilesUrl: '/api/map/tiles.pmtiles' }, tokens)
    expect((s.sources.base as { url: string }).url).toBe(`pmtiles://${location.origin}/api/map/tiles.pmtiles`)
  })

  it('draws coastlines in the primary colour and paints land with the background token', () => {
    const s = tacticalStyle('dark', online, tokens)
    const layer = (id: string) => s.layers.find((l) => l.id === id) as { paint?: Record<string, unknown> } | undefined
    expect(layer('background')?.paint?.['background-color']).toBe('#080b12')
    expect(layer('coastline')?.paint?.['line-color']).toBe('#4cc3ff')
  })

  it('drops label layers when no glyphs are configured', () => {
    const s = tacticalStyle('dark', { ...online, glyphsUrl: null }, tokens)
    expect(s.glyphs).toBeUndefined()
    expect(s.layers.some((l) => l.type === 'symbol')).toBe(false)
  })

  it('has only a background when the source is None', () => {
    const s = tacticalStyle('dark', { ...online, source: 'None', tileJsonUrl: null }, tokens)
    expect(Object.keys(s.sources)).toEqual([])
    expect(s.layers.map((l) => l.id)).toEqual(['background'])
  })
})
