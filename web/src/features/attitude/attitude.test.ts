import { describe, expect, it } from 'vitest'
import type { FlightTrack } from '../../api/types'
import { destination } from '../map/track'
import { angleDelta, attitudeAt, deriveAttitude } from './attitude'

/** A track sampled every 100 ms from a step function (heading, speed, climb at each time). */
function fly(seconds: number, step: (t: number) => { turnDegPerS: number; speed: number; climb: number }): FlightTrack {
  const track: FlightTrack = { t: [], lat: [], lon: [], altM: [] }
  let lat = 44
  let lon = 26
  let alt = 3000
  let heading = 0
  for (let i = 0; i <= seconds * 10; i++) {
    const s = step(i / 10)
    track.t.push(i * 100)
    track.lat.push(lat)
    track.lon.push(lon)
    track.altM.push(alt)
    heading = (heading + s.turnDegPerS * 0.1 + 360) % 360
    ;[lat, lon] = destination(lat, lon, heading, s.speed * 0.1)
    alt += s.climb * 0.1
  }
  return track
}

describe('derived attitude', () => {
  it('is wings level and pitch flat in straight and level flight', () => {
    const a = attitudeAt(deriveAttitude(fly(20, () => ({ turnDegPerS: 0, speed: 230, climb: 0 }))), 10_000)!
    expect(a.roll).toBeCloseTo(0, 1)
    expect(a.pitch).toBeCloseTo(0, 1)
    expect(a.heading).toBeCloseTo(0, 0)
    expect(a.groundSpeed).toBeCloseTo(230, 0)
    expect(a.loadFactor).toBeCloseTo(1, 2)
  })

  it('banks as a coordinated turn needs: 3°/s at 230 m/s is about 51° right', () => {
    const a = attitudeAt(deriveAttitude(fly(30, () => ({ turnDegPerS: 3, speed: 230, climb: 0 }))), 15_000)!
    expect(a.turnRate).toBeCloseTo(3, 1)
    expect(a.roll).toBeCloseTo(50.8, 0)
    expect(a.loadFactor).toBeCloseTo(1 / Math.cos((50.8 * Math.PI) / 180), 1)
  })

  it('banks left in a left turn and pitches up in a climb', () => {
    const a = attitudeAt(deriveAttitude(fly(30, () => ({ turnDegPerS: -2, speed: 100, climb: 10 }))), 15_000)!
    expect(a.roll).toBeLessThan(-15)
    expect(a.pitch).toBeCloseTo((Math.atan2(10, 100) * 180) / Math.PI, 0)
    expect(a.verticalSpeed).toBeCloseTo(10, 1)
  })

  it('shows a roll rate while rolling into a turn', () => {
    const series = deriveAttitude(fly(30, (t) => ({ turnDegPerS: t < 10 ? 0 : t < 14 ? (t - 10) * 0.75 : 3, speed: 230, climb: 0 })))
    expect(attitudeAt(series, 12_000)!.rollRate).toBeGreaterThan(5)
    expect(Math.abs(attitudeAt(series, 25_000)!.rollRate)).toBeLessThan(1)
  })

  it('interpolates heading the short way through north', () => {
    const series = { ...deriveAttitude(fly(1, () => ({ turnDegPerS: 0, speed: 100, climb: 0 }))) }
    series.t = [0, 1000]
    series.heading = [350, 10]
    for (const k of ['pitch', 'roll', 'rollRate', 'pitchRate', 'turnRate', 'verticalSpeed', 'groundSpeed', 'loadFactor'] as const) series[k] = [0, 0]
    expect(attitudeAt(series, 500)!.heading).toBeCloseTo(0, 5)
    expect(angleDelta(350, 10)).toBe(20)
    expect(angleDelta(10, 350)).toBe(-20)
  })

  it('has no attitude without samples', () => {
    expect(attitudeAt(deriveAttitude({ t: [], lat: [], lon: [], altM: [] }), 0)).toBeNull()
  })
})
