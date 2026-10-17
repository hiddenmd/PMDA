import type { FlightTrack } from '../../api/types'

const EARTH_RADIUS_M = 6_371_000
const rad = (deg: number) => (deg * Math.PI) / 180
const deg = (r: number) => (r * 180) / Math.PI

export interface TrackPoint {
  lat: number
  lon: number
  altM: number
}

/** Index of the last sample at or before `ms`, or -1 when `ms` is before the first sample. */
export function sampleIndex(track: FlightTrack, ms: number): number {
  let lo = 0
  let hi = track.t.length - 1
  let found = -1
  while (lo <= hi) {
    const mid = (lo + hi) >> 1
    if (track.t[mid] <= ms) {
      found = mid
      lo = mid + 1
    } else {
      hi = mid - 1
    }
  }
  return found
}

/** Position at `ms`, interpolated between the neighbouring samples and held at the ends; null without samples. */
export function positionAt(track: FlightTrack, ms: number): TrackPoint | null {
  const n = track.t.length
  if (n === 0) return null
  const i = sampleIndex(track, ms)
  if (i < 0) return { lat: track.lat[0], lon: track.lon[0], altM: track.altM[0] }
  if (i >= n - 1) return { lat: track.lat[n - 1], lon: track.lon[n - 1], altM: track.altM[n - 1] }
  const f = (ms - track.t[i]) / Math.max(1, track.t[i + 1] - track.t[i])
  const mix = (a: number[]) => a[i] + (a[i + 1] - a[i]) * f
  return { lat: mix(track.lat), lon: mix(track.lon), altM: mix(track.altM) }
}

/** The segment (pair of sample indexes) around `ms`, clamped to the track; null with fewer than two samples. */
function segment(track: FlightTrack, ms: number): [number, number] | null {
  const n = track.t.length
  if (n < 2) return null
  const i = Math.min(Math.max(sampleIndex(track, ms), 0), n - 2)
  return [i, i + 1]
}

/** Initial great-circle bearing from point 1 to point 2, degrees true, 0–360. */
export function bearingDeg(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const y = Math.sin(rad(lon2 - lon1)) * Math.cos(rad(lat2))
  const x = Math.cos(rad(lat1)) * Math.sin(rad(lat2)) - Math.sin(rad(lat1)) * Math.cos(rad(lat2)) * Math.cos(rad(lon2 - lon1))
  return (deg(Math.atan2(y, x)) + 360) % 360
}

export function distanceM(lat1: number, lon1: number, lat2: number, lon2: number): number {
  const a = Math.sin(rad(lat2 - lat1) / 2) ** 2 + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(rad(lon2 - lon1) / 2) ** 2
  return 2 * EARTH_RADIUS_M * Math.asin(Math.min(1, Math.sqrt(a)))
}

/** Track over the ground at `ms`, degrees true, from the neighbouring samples; null when it cannot be told. */
export function headingAt(track: FlightTrack, ms: number): number | null {
  const s = segment(track, ms)
  if (!s) return null
  const [a, b] = s
  if (track.lat[a] === track.lat[b] && track.lon[a] === track.lon[b]) return null
  return bearingDeg(track.lat[a], track.lon[a], track.lat[b], track.lon[b])
}

/** Ground speed at `ms` in m/s, from the neighbouring samples; null when it cannot be told. */
export function groundSpeedAt(track: FlightTrack, ms: number): number | null {
  const s = segment(track, ms)
  if (!s) return null
  const [a, b] = s
  const dt = (track.t[b] - track.t[a]) / 1000
  if (dt <= 0) return null
  return distanceM(track.lat[a], track.lon[a], track.lat[b], track.lon[b]) / dt
}

/** Point `distanceM` metres from a position along a true bearing (for lines of bearing). */
export function destination(lat: number, lon: number, bearing: number, distance: number): [number, number] {
  const d = distance / EARTH_RADIUS_M
  const b = rad(bearing)
  const lat2 = Math.asin(Math.sin(rad(lat)) * Math.cos(d) + Math.cos(rad(lat)) * Math.sin(d) * Math.cos(b))
  const lon2 = rad(lon) + Math.atan2(Math.sin(b) * Math.sin(d) * Math.cos(rad(lat)), Math.cos(d) - Math.sin(rad(lat)) * Math.sin(lat2))
  return [deg(lat2), deg(lon2)]
}

/** 44°25.62'N style: degrees and decimal minutes, as navigation displays show them. */
export function formatLatLon(value: number, axis: 'lat' | 'lon'): string {
  const hemisphere = axis === 'lat' ? (value >= 0 ? 'N' : 'S') : value >= 0 ? 'E' : 'W'
  const abs = Math.abs(value)
  let degrees = Math.floor(abs)
  let minutes = Number(((abs - degrees) * 60).toFixed(2))
  if (minutes >= 60) {
    degrees += 1
    minutes = 0
  }
  return `${String(degrees).padStart(axis === 'lat' ? 2 : 3, '0')}°${minutes.toFixed(2).padStart(5, '0')}'${hemisphere}`
}

export const metresToFeet = (m: number) => m / 0.3048
export const msToKnots = (ms: number) => ms * 1.943844
