import type { FlightTrack } from '../../api/types'
import { bearingDeg, distanceM } from '../map/track'

const G = 9.80665
const RAD = Math.PI / 180

/**
 * Aircraft attitude and rates over time, as parallel arrays on the track's time base. Angles in
 * degrees, rates in degrees per second, speeds in m/s. Heading is the track over the ground.
 */
export interface AttitudeSeries {
  t: number[]
  pitch: number[]
  roll: number[]
  heading: number[]
  rollRate: number[]
  pitchRate: number[]
  turnRate: number[]
  verticalSpeed: number[]
  groundSpeed: number[]
  loadFactor: number[]
}

export type AttitudeKey = Exclude<keyof AttitudeSeries, 't'>

export interface Attitude {
  pitch: number
  roll: number
  heading: number
  rollRate: number
  pitchRate: number
  turnRate: number
  verticalSpeed: number
  groundSpeed: number
  loadFactor: number
}

/** Signed smallest difference to − from, in (−180, 180]. */
export function angleDelta(from: number, to: number): number {
  const d = (((to - from) % 360) + 360) % 360
  return d > 180 ? d - 360 : d
}

/** Indexes of the samples spanning ±halfMs around sample i (at least its neighbours). */
function windowAround(t: readonly number[], i: number, halfMs: number, cursor: { lo: number; hi: number }): [number, number] {
  const n = t.length
  while (cursor.lo < i && t[i] - t[cursor.lo] > halfMs) cursor.lo++
  if (cursor.hi < i) cursor.hi = i
  while (cursor.hi + 1 < n && t[cursor.hi + 1] - t[i] <= halfMs) cursor.hi++
  let lo = Math.min(cursor.lo, i)
  let hi = cursor.hi
  if (lo === hi) {
    lo = Math.max(0, i - 1)
    hi = Math.min(n - 1, i + 1)
  }
  return [lo, hi]
}

/** Central difference of `values` over the window around each sample, per second. */
function rateOf(t: readonly number[], values: readonly number[], halfMs: number, wrap = false): number[] {
  const cursor = { lo: 0, hi: 0 }
  return t.map((_, i) => {
    const [lo, hi] = windowAround(t, i, halfMs, cursor)
    const dt = (t[hi] - t[lo]) / 1000
    if (dt <= 0) return 0
    return (wrap ? angleDelta(values[lo], values[hi]) : values[hi] - values[lo]) / dt
  })
}

/**
 * Attitude derived from the navigation track, for recordings that carry no attitude of their own:
 * heading is the track over the ground, pitch the flight-path angle (climb over ground speed), and
 * bank the angle a coordinated turn at that speed and turn rate needs (tan φ = V·ω / g). Load
 * factor follows from bank and the rate of change of the flight path. Differences are taken over
 * ±`halfWindowMs` to smooth sampling noise. Sideslip and angle of attack cannot be known this way.
 */
export function deriveAttitude(track: FlightTrack, halfWindowMs = 1_500): AttitudeSeries {
  const { t, lat, lon, altM } = track
  const n = t.length
  const heading = new Array<number>(n).fill(0)
  const groundSpeed = new Array<number>(n).fill(0)
  const verticalSpeed = new Array<number>(n).fill(0)

  const cursor = { lo: 0, hi: 0 }
  for (let i = 0; i < n; i++) {
    const [lo, hi] = windowAround(t, i, halfWindowMs, cursor)
    const dt = (t[hi] - t[lo]) / 1000
    const distance = distanceM(lat[lo], lon[lo], lat[hi], lon[hi])
    heading[i] = distance > 0.5 ? bearingDeg(lat[lo], lon[lo], lat[hi], lon[hi]) : i > 0 ? heading[i - 1] : 0
    groundSpeed[i] = dt > 0 ? distance / dt : 0
    verticalSpeed[i] = dt > 0 ? (altM[hi] - altM[lo]) / dt : 0
  }

  const turnRate = rateOf(t, heading, halfWindowMs, true)
  const pitch = verticalSpeed.map((w, i) => Math.atan2(w, groundSpeed[i]) / RAD)
  const roll = turnRate.map((r, i) => Math.max(-85, Math.min(85, Math.atan((groundSpeed[i] * r * RAD) / G) / RAD)))
  const rollRate = rateOf(t, roll, halfWindowMs)
  const pitchRate = rateOf(t, pitch, halfWindowMs)
  const loadFactor = roll.map((phi, i) => {
    const speed = Math.hypot(groundSpeed[i], verticalSpeed[i])
    return Math.cos(pitch[i] * RAD) / Math.cos(phi * RAD) + (speed * pitchRate[i] * RAD) / G
  })

  return { t: [...t], pitch, roll, heading, rollRate, pitchRate, turnRate, verticalSpeed, groundSpeed, loadFactor }
}

function indexAt(t: readonly number[], ms: number): number {
  let lo = 0
  let hi = t.length - 1
  let found = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (t[mid] <= ms) {
      found = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return found
}

const KEYS: AttitudeKey[] = ['pitch', 'roll', 'heading', 'rollRate', 'pitchRate', 'turnRate', 'verticalSpeed', 'groundSpeed', 'loadFactor']

/** Attitude at `ms`, interpolated between samples (heading the short way round); null without samples. */
export function attitudeAt(series: AttitudeSeries, ms: number): Attitude | null {
  const n = series.t.length
  if (n === 0) return null
  const i = Math.min(Math.max(indexAt(series.t, ms), 0), n - 1)
  const j = Math.min(i + 1, n - 1)
  const f = j === i ? 0 : Math.max(0, Math.min(1, (ms - series.t[i]) / Math.max(1, series.t[j] - series.t[i])))
  const out = {} as Attitude
  for (const key of KEYS) {
    const a = series[key][i]
    const b = series[key][j]
    out[key] = key === 'heading' ? (((a + angleDelta(a, b) * f) % 360) + 360) % 360 : a + (b - a) * f
  }
  return out
}
