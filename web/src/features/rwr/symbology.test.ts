import { describe, expect, it } from 'vitest'
import type { ActiveEmitter } from '../playback/playbackView'
import { buildThreats, classify, dispensedBy, resolveProfile, type SymbologyProfile } from './symbology'

const profile = resolveProfile({
  id: 'T',
  name: 'Test',
  platform: 'FixedWing',
  display: { phosphor: '#0f0', background: '#000', rings: [0.5, 1], ownshipCircle: 0.2, maxSymbols: 3 },
  placement: { mode: 'Lethality', critical: 0.3, lethal: 0.6, nonLethal: 0.9 },
  threats: [
    { code: '29', class: 'Airborne', lethality: 'Critical', emitterIds: [7] },
    { code: '08', class: 'Sam', lethality: 'Critical', frequencyMHz: [9000, 10600], priUs: [100, 400] },
    { code: 'S', class: 'EarlyWarning', lethality: 'NonLethal', frequencyMHz: [2700, 3300] },
  ],
  unknown: { code: 'U', class: 'Unknown', lethality: 'NonLethal' },
  symbols: {
    byClass: { Sam: 'None', Aaa: 'None', Airborne: 'Hat', EarlyWarning: 'None', Naval: 'Boat', Unknown: 'None' },
    priority: 'Shield',
    lock: 'Circle',
    lockMinHitsPerSecond: 12,
    newThreatMs: 2000,
  },
} satisfies SymbologyProfile)

const emitter = (o: Partial<ActiveEmitter>): ActiveEmitter => ({
  emitterId: 1,
  hits: 1,
  frequencyMHz: 9500,
  priUs: 200,
  pulseWidthUs: 1,
  bearingDeg: 45,
  amplitudeDb: -50,
  ageMs: 0,
  hitsPerSecond: 5,
  presentForMs: 10_000,
  ...o,
})

describe('RWR symbology', () => {
  it('classifies by the first matching rule; explicit ids win; no match is unknown', () => {
    expect(classify(emitter({}), profile).code).toBe('08')
    expect(classify(emitter({ emitterId: 7 }), profile).code).toBe('29')
    expect(classify(emitter({ frequencyMHz: 9500, priUs: 900 }), profile).code).toBe('U') // PRI out of range
    expect(classify(emitter({ frequencyMHz: 3000, priUs: 3000 }), profile).code).toBe('S')
  })

  it('orders by lethality, then tracking, then strength, and limits the count', () => {
    const threats = buildThreats(
      [
        emitter({ emitterId: 1, frequencyMHz: 3000, amplitudeDb: -10 }), // S, non-lethal, strongest
        emitter({ emitterId: 2, amplitudeDb: -60 }), // 08 critical, weak
        emitter({ emitterId: 3, amplitudeDb: -70, hitsPerSecond: 20 }), // 08 critical, locked
        emitter({ emitterId: 4, frequencyMHz: 3000 }), // dropped: over maxSymbols
      ],
      profile,
    )

    expect(threats.map((t) => t.emitterId)).toEqual([3, 2, 1])
    expect(threats[0]).toMatchObject({ priority: true, locked: true, shapes: ['Circle', 'Shield'], radius: 0.3 })
    expect(threats[1]).toMatchObject({ priority: false, locked: false, shapes: [], radius: 0.3 })
    expect(threats[2]).toMatchObject({ code: 'S', radius: 0.9 })
  })

  it('decorates by class and flags new threats', () => {
    const [airborne] = buildThreats([emitter({ emitterId: 7, presentForMs: 500 })], profile)

    expect(airborne.code).toBe('29')
    expect(airborne.shapes).toEqual(['Hat', 'Shield'])
    expect(airborne.isNew).toBe(true)
  })

  it('places by signal strength in amplitude mode, inside the own-ship circle margin', () => {
    const amplitude = { ...profile, placement: { ...profile.placement, mode: 'Amplitude' as const } }
    const [strong, weak] = buildThreats([emitter({ emitterId: 1, amplitudeDb: -10 }), emitter({ emitterId: 2, amplitudeDb: -90 })], amplitude)

    expect(strong.radius).toBeCloseTo(0.26)
    expect(weak.radius).toBeCloseTo(0.94)
  })

  it('counts countermeasures dispensed up to the playhead', () => {
    const marks = [
      { t: 100, kind: 1, count: 2 },
      { t: 200, kind: 2, count: 1 },
      { t: 900, kind: 1, count: 3 },
    ]
    expect(dispensedBy(marks, 500)).toEqual({ chaff: 2, flares: 1 })
    expect(dispensedBy(marks, 50)).toEqual({ chaff: 0, flares: 0 })
  })
})

describe('RWR declutter', () => {
  it('keeps the priority symbol in place and moves overlapping ones apart', async () => {
    const { declutter } = await import('./rwrDraw')
    const threats = [
      { bearingDeg: 0, radius: 0.5 },
      { bearingDeg: 1, radius: 0.5 }, // almost on top of the first
      { bearingDeg: 180, radius: 0.5 }, // far away: untouched
    ]

    const [first, second, third] = declutter(threats, 100, 100, 100, 20)

    expect(first.x).toBeCloseTo(100)
    expect(first.y).toBeCloseTo(50)
    expect(Math.hypot(second.x - first.x, second.y - first.y)).toBeGreaterThanOrEqual(20)
    expect(third.y).toBeCloseTo(150)
  })
})
