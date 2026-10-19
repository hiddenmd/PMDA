import type * as GeoJSON from 'geojson'
import type { ThreatClass } from '../rwr/symbology'
import { destination } from './track'

/** Typical engagement range per class, for the "nominal range" rings — an assumption, labelled as such in the UI. */
export const NOMINAL_RANGE_M: Partial<Record<ThreatClass, number>> = { Sam: 25_000, Aaa: 4_000, Naval: 30_000 }

const RAD = Math.PI / 180

/** Closed ring of [lon, lat] around a centre, `steps` segments. */
export function circlePolygon(lat: number, lon: number, radiusM: number, steps = 96): [number, number][] {
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    const [la, lo] = destination(lat, lon, (360 * (i % steps)) / steps, radiusM)
    ring.push([lo, la])
  }
  return ring
}

/** Closed ellipse ring: semi-axes in metres, major axis `azimuthDeg` clockwise from north. */
export function ellipsePolygon(lat: number, lon: number, majorM: number, minorM: number, azimuthDeg: number, steps = 96): [number, number][] {
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    const a = (2 * Math.PI * (i % steps)) / steps
    const along = majorM * Math.cos(a)
    const across = minorM * Math.sin(a)
    const distance = Math.hypot(along, across)
    const bearing = azimuthDeg + Math.atan2(across, along) / RAD
    const [la, lo] = distance === 0 ? [lat, lon] : destination(lat, lon, ((bearing % 360) + 360) % 360, distance)
    ring.push([lo, la])
  }
  return ring
}

/** Closed pie slice from the centre: `widthDeg` wide starting at `fromDeg` (clockwise from north). */
export function wedgePolygon(lat: number, lon: number, radiusM: number, fromDeg: number, widthDeg: number, steps = 12): [number, number][] {
  const ring: [number, number][] = [[lon, lat]]
  for (let i = 0; i <= steps; i++) {
    const [la, lo] = destination(lat, lon, (((fromDeg + (widthDeg * i) / steps) % 360) + 360) % 360, radiusM)
    ring.push([lo, la])
  }
  ring.push([lon, lat])
  return ring
}

/** Meridians and parallels on whole multiples of `stepDeg` strictly inside the bounds. */
export function graticule(b: { west: number; south: number; east: number; north: number }, stepDeg: number): GeoJSON.FeatureCollection<GeoJSON.LineString> {
  const features: GeoJSON.Feature<GeoJSON.LineString>[] = []
  const round = (v: number) => Math.round(v * 1e6) / 1e6
  for (let lon = Math.ceil(b.west / stepDeg) * stepDeg; lon < b.east; lon += stepDeg)
    features.push({ type: 'Feature', properties: { kind: 'meridian' }, geometry: { type: 'LineString', coordinates: [[round(lon), b.south], [round(lon), b.north]] } })
  for (let lat = Math.ceil(b.south / stepDeg) * stepDeg; lat < b.north; lat += stepDeg)
    features.push({ type: 'Feature', properties: { kind: 'parallel' }, geometry: { type: 'LineString', coordinates: [[b.west, round(lat)], [b.east, round(lat)]] } })
  return { type: 'FeatureCollection', features }
}
