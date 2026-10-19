import { Euler, Quaternion, Vector3 } from 'three'
import type { FlightTrack } from '../../../api/types'
import { positionAt, sampleIndex, type TrackPoint } from '../../map/track'

const RAD = Math.PI / 180
const EARTH_RADIUS_M = 6_371_000

/**
 * Orientation of a model whose nose is −Z, up +Y and right wing +X, in the local frame
 * (x east, y up, z south): heading turns it about +Y, then pitch about X, then roll about Z.
 */
export function attitudeQuaternion(pitch: number, roll: number, heading: number): Quaternion {
  return new Quaternion().setFromEuler(new Euler(pitch * RAD, -heading * RAD, -roll * RAD, 'YXZ'))
}

/** `p` in metres from `origin`: x east, y up, z south (equirectangular; fine over a few km). */
export function toLocal(origin: TrackPoint, p: TrackPoint): [number, number, number] {
  const x = (p.lon - origin.lon) * RAD * EARTH_RADIUS_M * Math.cos(origin.lat * RAD)
  const z = -(p.lat - origin.lat) * RAD * EARTH_RADIUS_M
  return [x, p.altM - origin.altM, z]
}

/**
 * The flown path over the `windowMs` before `ms`, as xyz triples relative to the position at `ms`,
 * newest last and ending exactly at the playhead; at most `max` points (older samples thinned).
 */
export function trailPoints(track: FlightTrack, ms: number, windowMs = 60_000, max = 240): Float32Array {
  const here = positionAt(track, ms)
  if (!here) return new Float32Array(0)
  const last = sampleIndex(track, ms)
  let first = last
  while (first > 0 && track.t[first - 1] >= ms - windowMs) first--
  const indexes: number[] = []
  if (last >= 0) for (let i = Math.max(first, 0); i <= last; i++) if (track.t[i] >= ms - windowMs) indexes.push(i)
  const room = max - 1 // the playhead point is always kept
  const stride = Math.max(1, Math.ceil(indexes.length / room))
  const kept = indexes.filter((_, k) => (indexes.length - 1 - k) % stride === 0)
  const out = new Float32Array((kept.length + 1) * 3)
  kept.forEach((i, k) => out.set(toLocal(here, { lat: track.lat[i], lon: track.lon[i], altM: track.altM[i] }), k * 3))
  out.set([0, 0, 0], kept.length * 3)
  return out
}

export type CameraPreset = 'chase' | 'side' | 'top'

/** Distance (m), azimuth from the tail (deg, + to the right) and elevation (deg) of each preset. */
const PRESETS: Record<CameraPreset, { d: number; az: number; el: number; northUp?: boolean }> = {
  chase: { d: Math.hypot(16, 7.4, 39), az: Math.atan2(16, 39) / RAD, el: Math.asin(7.4 / Math.hypot(16, 7.4, 39)) / RAD },
  side: { d: 50, az: 90, el: 0 },
  top: { d: 60, az: 0, el: 89, northUp: true },
}

/** Camera position relative to the aircraft, in the local frame, for a preset plus the user's orbit (deg). */
export function cameraOffset(preset: CameraPreset, headingDeg: number, orbit: { yaw: number; pitch: number }): Vector3 {
  const p = PRESETS[preset]
  const az = (p.az - (p.northUp ? 0 : headingDeg) + orbit.yaw) * RAD
  const el = Math.min(89, Math.max(-10, p.el + orbit.pitch)) * RAD
  return new Vector3(Math.cos(el) * Math.sin(az), Math.sin(el), Math.cos(el) * Math.cos(az)).multiplyScalar(p.d)
}

/** Fraction of the remaining distance to cover this frame for exponential damping. */
export const damp = (rateBase: number, dtSec: number) => 1 - rateBase ** dtSec
