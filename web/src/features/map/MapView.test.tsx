import { render, screen } from '@testing-library/react'
import { beforeAll, describe, expect, it } from 'vitest'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { MapView, type MapReadout } from './MapView'

beforeAll(() => {
  globalThis.ResizeObserver ??= class {
    observe() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
})

const actor = { getSnapshot: () => ({ context: { positionMs: 0, frames: [], speed: 1 } }), send: () => {} } as unknown as PlaybackActor
const settings = { source: 'OpenFreeMap', tileJsonUrl: 'https://tiles.openfreemap.org/planet', glyphsUrl: null, pmtilesUrl: null, terrainUrl: null, attribution: 'OSM', maxZoom: 15 }
const track = { t: [0, 1000], lat: [44, 44.01], lon: [26, 26], altM: [100, 100] }

function view(overrides = {}) {
  const readout = { current: { lat: null, lon: null, alt: null, heading: null, speed: null } as MapReadout }
  return render(
    <MapView
      actor={actor}
      track={track}
      alerts={[]}
      settings={settings}
      profile={undefined}
      startUtc="2026-03-14T09:30:00Z"
      follow
      fitRequest={0}
      onFollowChange={() => {}}
      readout={readout}
      fixes={[]}
      mode="2d"
      {...overrides}
    />,
  )
}

describe('MapView', () => {
  it('says the map needs WebGL where it is unavailable (jsdom)', async () => {
    view()
    expect(await screen.findByText(/map needs WebGL/i)).toBeInTheDocument()
  })

  it('keeps the track-only note when no base map is configured', async () => {
    view({ settings: { ...settings, source: 'None', tileJsonUrl: null } })
    expect(await screen.findByText('No base map configured: track only.')).toBeInTheDocument()
  })

  it('labels the map for assistive technology', () => {
    view()
    expect(screen.getByRole('application', { name: /flight map/i })).toBeInTheDocument()
  })
})
