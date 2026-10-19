import { describe, expect, it } from 'vitest'
import type { ActiveEmitter } from '../playback/playbackView'
import { buildThreats, resolveProfile, type SymbologyProfile } from './symbology'
import { drawTactical, lethalityColor, roseLabel, statusParts, type TacticalTokens } from './rwrTactical'

const tokens: TacticalTokens = {
  background: '#080b12',
  foreground: '#cfd8e6',
  muted: '#8fa3bf',
  border: '#1a2436',
  borderStrong: '#22324d',
  primary: '#4cc3ff',
  critical: '#ff5a5a',
  warning: '#fb923c',
}

const profile = resolveProfile({
  id: 'T',
  name: 'Test',
  platform: 'FixedWing',
  display: { phosphor: '#0f0', background: '#000', rings: [0.5, 1], ownshipCircle: 0.2, maxSymbols: 3, countermeasures: { chaff: 30, flares: 30 } },
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

/** A 2D context that records what was written and how often things were stroked or filled. */
function recordingContext() {
  const calls = { text: [] as string[], stroke: 0, fill: 0 }
  const target: Record<string, unknown> = {
    measureText: (t: string) => ({ width: t.length * 6 }),
    fillText: (t: string) => void calls.text.push(t),
    stroke: () => void calls.stroke++,
    fill: () => void calls.fill++,
    createRadialGradient: () => ({ addColorStop: () => {} }),
  }
  const ctx = new Proxy(target, {
    get: (t, key: string) => (key in t ? t[key] : () => {}),
    set: (t, key: string, value) => {
      t[key] = value
      return true
    },
  }) as unknown as CanvasRenderingContext2D
  return { ctx, calls }
}

describe('tactical RWR', () => {
  it('colours threats by lethality like the map', () => {
    expect(lethalityColor('Critical', tokens)).toBe('#ff5a5a')
    expect(lethalityColor('Lethal', tokens)).toBe('#fb923c')
    expect(lethalityColor('NonLethal', tokens)).toBe('#8fa3bf')
  })

  it('labels the rose in three-digit relative degrees', () => {
    expect(roseLabel(0)).toBe('000')
    expect(roseLabel(30)).toBe('030')
    expect(roseLabel(330)).toBe('330')
  })

  it('summarises the scope: count, priority threat and countermeasures left', () => {
    const threats = buildThreats([emitter({ emitterId: 3, hitsPerSecond: 20, bearingDeg: 44.6 }), emitter({ emitterId: 1, frequencyMHz: 3000 })], profile)
    expect(statusParts(threats, { chaff: 28, flares: 30 })).toEqual(['2 THREATS', 'PRI 08 045°', 'LOCK', 'CHAFF 28', 'FLARE 30'])
    expect(statusParts([], null)).toEqual(['NO THREATS'])
  })

  it('draws the rose, own-ship and every threat code', () => {
    const { ctx, calls } = recordingContext()
    const threats = buildThreats([emitter({ emitterId: 3, hitsPerSecond: 20 }), emitter({ emitterId: 7, bearingDeg: 200 })], profile)
    drawTactical(ctx, 600, 600, { profile, threats, remaining: { chaff: 30, flares: 30 }, persistenceMs: 4000, now: 0, still: true }, tokens)
    expect(calls.text).toEqual(expect.arrayContaining(['000', '090', '180', '270', '08', '29', '2 THREATS']))
    expect(calls.stroke).toBeGreaterThan(36) // rings, ticks, own-ship, symbols
  })

  it('skips a blinking new threat in its off phase, unless motion is reduced', () => {
    const fresh = buildThreats([emitter({ emitterId: 3, presentForMs: 100 })], profile)
    const off = recordingContext()
    drawTactical(off.ctx, 600, 600, { profile, threats: fresh, remaining: null, persistenceMs: 4000, now: 250, still: false }, tokens)
    expect(off.calls.text).not.toContain('08')
    const still = recordingContext()
    drawTactical(still.ctx, 600, 600, { profile, threats: fresh, remaining: null, persistenceMs: 4000, now: 250, still: true }, tokens)
    expect(still.calls.text).toContain('08')
  })
})
