import { describe, expect, it } from 'vitest'
import { alignOnTime, graphTitle, paramLabel } from './graphKinds'

describe('graph kinds', () => {
  it('aligns emitter series on one time axis with gaps as null', () => {
    const { x, ys } = alignOnTime([
      { t: [500, 1500], v: [1, 2] },
      { t: [1500, 2500], v: [10, 20] },
    ])

    expect(x).toEqual([500, 1500, 2500])
    expect(ys).toEqual([
      [1, 2, null],
      [null, 10, 20],
    ])
  })

  it('names graphs and axes', () => {
    expect(graphTitle({ kind: 'parameter', param: 'Pri', x: 'Frequency', y: 'Pri' })).toBe('PRI vs time')
    expect(graphTitle({ kind: 'scatter', param: 'Pri', x: 'Frequency', y: 'PulseWidth' })).toBe('Pulse width vs Frequency')
    expect(graphTitle({ kind: 'profile', param: 'Pri', x: 'Frequency', y: 'Pri' })).toBe('Flight profile')
    expect(paramLabel('Bearing')).toBe('Bearing (°)')
  })
})
