import { describe, expect, it } from 'vitest'
import { distanceM } from './track'
import { circlePolygon, ellipsePolygon, graticule, NOMINAL_RANGE_M, wedgePolygon } from './geo'

describe('geo helpers', () => {
  it('nominal ranges by class', () => {
    expect(NOMINAL_RANGE_M).toEqual({ Sam: 25_000, Aaa: 4_000, Naval: 30_000 })
  })

  it('circle points lie at the radius and the ring is closed', () => {
    const ring = circlePolygon(44, 26, 25_000, 64)
    expect(ring.length).toBe(65)
    expect(ring[0]).toEqual(ring[64])
    for (const [lon, lat] of ring) expect(distanceM(44, 26, lat, lon)).toBeCloseTo(25_000, -1)
  })

  it('ellipse: the major axis points along the azimuth', () => {
    const ring = ellipsePolygon(44, 26, 2_000, 500, 90, 72) // major axis east-west
    const far = Math.max(...ring.map(([lon, lat]) => distanceM(44, 26, lat, lon)))
    const near = Math.min(...ring.map(([lon, lat]) => distanceM(44, 26, lat, lon)))
    expect(far).toBeCloseTo(2_000, -1)
    expect(near).toBeCloseTo(500, -1)
    const east = ring.reduce((a, b) => (b[0] > a[0] ? b : a))
    expect(distanceM(44, 26, east[1], east[0])).toBeCloseTo(2_000, -1)
  })

  it('wedge spans its angle from the centre', () => {
    const w = wedgePolygon(44, 26, 10_000, 0, 30, 6)
    expect(w[0]).toEqual([26, 44])
    expect(w[w.length - 1]).toEqual([26, 44])
    expect(w.length).toBe(6 + 1 + 2)
  })

  it('graticule lines every step inside the bounds', () => {
    const g = graticule({ west: 25.1, south: 43.9, east: 26.9, north: 44.6 }, 0.5)
    const meridians = g.features.filter((f) => f.properties?.kind === 'meridian').map((f) => f.geometry.coordinates[0][0])
    const parallels = g.features.filter((f) => f.properties?.kind === 'parallel').map((f) => f.geometry.coordinates[0][1])
    expect(meridians).toEqual([25.5, 26, 26.5])
    expect(parallels).toEqual([44, 44.5])
  })
})
