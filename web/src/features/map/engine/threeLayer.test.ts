import { describe, expect, it } from 'vitest'
import { dropLineTimes, localFrame } from './threeLayer'

describe('flight layer helpers', () => {
  it('local frame: east +x, up +y (MSL altitude), south +z', () => {
    const [x, y, z] = localFrame({ lat: 44, lon: 26 }, { lat: 43.999, lon: 26.001, altM: 1500 })
    expect(x).toBeGreaterThan(0)
    expect(y).toBe(1500)
    expect(z).toBeGreaterThan(0)
  })

  it('drop lines every 30 s across the track span', () => {
    expect(dropLineTimes([0, 10_000, 95_000])).toEqual([0, 30_000, 60_000, 90_000])
  })
})
