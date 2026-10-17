import { describe, expect, it } from 'vitest'
import { timeTicks } from './timeAxis'

const at = (iso: string) => Date.parse(iso)

describe('time axis', () => {
  it('puts ticks on round UTC times between start and end', () => {
    // 45 min flight from 09:31:12Z: 5-minute ticks from 09:35Z, none near the ends
    const ticks = timeTicks(at('2026-03-14T09:31:12Z'), 45 * 60_000, 10)
    expect(ticks.map((t) => t.label)).toEqual(['09:35Z', '09:40Z', '09:45Z', '09:50Z', '09:55Z', '10:00Z', '10:05Z', '10:10Z'])
    expect(ticks[0].offsetMs).toBe(3 * 60_000 + 48_000)
  })

  it('picks a coarser step for longer flights and fewer ticks', () => {
    const ticks = timeTicks(at('2026-03-14T08:00:00Z'), 6 * 3_600_000, 6)
    expect(ticks.map((t) => t.label)).toEqual(['09:00Z', '10:00Z', '11:00Z', '12:00Z', '13:00Z'])
  })

  it('shows seconds when ticks are under a minute apart', () => {
    const ticks = timeTicks(at('2026-03-14T09:30:00Z'), 2 * 60_000, 8)
    expect(ticks[0].label).toBe('09:30:30Z')
  })

  it('has no ticks for an empty flight', () => {
    expect(timeTicks(0, 0, 10)).toEqual([])
  })
})
