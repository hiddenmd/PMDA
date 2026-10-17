import { describe, expect, it } from 'vitest'
import type { FlightTrack } from '../../api/types'
import { bearingDeg, destination, distanceM, formatLatLon, groundSpeedAt, headingAt, positionAt, sampleIndex } from './track'

// Due north at about 0.01° latitude (1.11 km) per 5 s, then due east.
const track: FlightTrack = {
  t: [0, 5_000, 10_000],
  lat: [44.0, 44.01, 44.01],
  lon: [26.0, 26.0, 26.02],
  altM: [1000, 2000, 2000],
}
const empty: FlightTrack = { t: [], lat: [], lon: [], altM: [] }

describe('track', () => {
  it('finds the sample at or before a time', () => {
    expect(sampleIndex(track, -1)).toBe(-1)
    expect(sampleIndex(track, 0)).toBe(0)
    expect(sampleIndex(track, 4_999)).toBe(0)
    expect(sampleIndex(track, 5_000)).toBe(1)
    expect(sampleIndex(track, 99_000)).toBe(2)
  })

  it('interpolates position between samples and holds it at the ends', () => {
    const mid = positionAt(track, 2_500)!
    expect(mid.lat).toBeCloseTo(44.005)
    expect(mid.altM).toBeCloseTo(1500)
    expect(positionAt(track, -100)).toEqual({ lat: 44, lon: 26, altM: 1000 })
    expect(positionAt(track, 50_000)).toEqual({ lat: 44.01, lon: 26.02, altM: 2000 })
    expect(positionAt(empty, 0)).toBeNull()
  })

  it('takes heading and ground speed from the segment at the playhead', () => {
    expect(headingAt(track, 1_000)).toBeCloseTo(0, 0)
    expect(headingAt(track, 7_000)).toBeCloseTo(90, 0)
    expect(headingAt(track, 60_000)).toBeCloseTo(90, 0) // past the end: last segment
    expect(groundSpeedAt(track, 1_000)).toBeCloseTo(1111.95 / 5, 0)
    expect(headingAt(empty, 0)).toBeNull()
    expect(groundSpeedAt({ t: [0], lat: [1], lon: [1], altM: [0] }, 0)).toBeNull()
  })

  it('does great-circle bearing, distance and destination consistently', () => {
    expect(bearingDeg(0, 0, 0, 1)).toBeCloseTo(90)
    expect(bearingDeg(0, 0, -1, 0)).toBeCloseTo(180)
    const [lat, lon] = destination(44, 26, 45, 10_000)
    expect(distanceM(44, 26, lat, lon)).toBeCloseTo(10_000, 0)
    expect(bearingDeg(44, 26, lat, lon)).toBeCloseTo(45, 1)
  })

  it('formats coordinates as degrees and decimal minutes', () => {
    expect(formatLatLon(44.427, 'lat')).toBe("44°25.62'N")
    expect(formatLatLon(-0.5, 'lat')).toBe("00°30.00'S")
    expect(formatLatLon(26.1, 'lon')).toBe("026°06.00'E")
    expect(formatLatLon(-122.99999, 'lon')).toBe("123°00.00'W") // minutes rounding up carries into degrees
  })
})
