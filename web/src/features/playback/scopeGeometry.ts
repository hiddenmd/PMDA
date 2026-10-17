/**
 * RWR-style scope geometry. 0° is the aircraft nose (top), bearings run clockwise. Following RWR
 * convention, a stronger signal (usually a closer threat) is drawn nearer the centre.
 */

export const AMPLITUDE_STRONG_DB = -10
export const AMPLITUDE_WEAK_DB = -90
/** Keep the strongest symbols off the exact centre so they stay readable. */
export const MIN_RADIUS_FRACTION = 0.14

export function radiusFraction(amplitudeDb: number): number {
  const t = (AMPLITUDE_STRONG_DB - amplitudeDb) / (AMPLITUDE_STRONG_DB - AMPLITUDE_WEAK_DB)
  return Math.min(1, Math.max(MIN_RADIUS_FRACTION, t))
}

export function scopePoint(bearingDeg: number, amplitudeDb: number, cx: number, cy: number, radius: number) {
  const r = radius * radiusFraction(amplitudeDb)
  const theta = (bearingDeg * Math.PI) / 180
  return { x: cx + r * Math.sin(theta), y: cy - r * Math.cos(theta) }
}

export type BandShape = 'triangle' | 'square' | 'diamond' | 'circle'

export interface Band {
  label: string
  shape: BandShape
}

/** Radar band by frequency; each band has its own symbol so the scope never relies on colour alone. */
export function bandOf(frequencyMHz: number): Band {
  if (frequencyMHz < 4_000) return { label: 'S', shape: 'triangle' }
  if (frequencyMHz < 8_000) return { label: 'C', shape: 'square' }
  if (frequencyMHz < 12_000) return { label: 'X', shape: 'diamond' }
  return { label: 'Ku', shape: 'circle' }
}
