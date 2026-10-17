import { describe, expect, it } from 'vitest'
import { bandOf, MIN_RADIUS_FRACTION, radiusFraction, scopePoint } from './scopeGeometry'

describe('scope geometry', () => {
  it('puts the nose at the top and runs bearings clockwise', () => {
    const at = (bearing: number) => scopePoint(bearing, -90, 100, 100, 50)
    expect(at(0).x).toBeCloseTo(100)
    expect(at(0).y).toBeCloseTo(50) // up
    expect(at(90).x).toBeCloseTo(150) // right
    expect(at(180).y).toBeCloseTo(150) // down
    expect(at(270).x).toBeCloseTo(50) // left
  })

  it('draws stronger signals nearer the centre, within limits', () => {
    expect(radiusFraction(-90)).toBe(1)
    expect(radiusFraction(-50)).toBeCloseTo(0.5)
    expect(radiusFraction(-10)).toBe(MIN_RADIUS_FRACTION)
    expect(radiusFraction(0)).toBe(MIN_RADIUS_FRACTION)
    expect(radiusFraction(-120)).toBe(1)
  })

  it('maps frequency to a band with its own symbol', () => {
    expect(bandOf(3_000)).toEqual({ label: 'S', shape: 'triangle' })
    expect(bandOf(5_500)).toEqual({ label: 'C', shape: 'square' })
    expect(bandOf(9_400)).toEqual({ label: 'X', shape: 'diamond' })
    expect(bandOf(15_000)).toEqual({ label: 'Ku', shape: 'circle' })
  })
})
