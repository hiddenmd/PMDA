import type { components } from '../../api/schema'
import type { ActiveEmitter } from '../playback/playbackView'
import { radiusFraction } from '../playback/scopeGeometry'

type Schemas = components['schemas']
export type SymbologyProfile = Schemas['SymbologyProfile']
export type ThreatRule = Schemas['ThreatRule']
export type ThreatClass = Schemas['ThreatClass']
export type Lethality = Schemas['Lethality']
export type SymbolShape = Schemas['SymbolShape']
export type Legend = Schemas['Legend']
export type Platform = Schemas['Platform']

/** A profile with every default filled in, so drawing code never guesses. */
export interface ResolvedProfile {
  id: string
  name: string
  platform: Platform
  display: {
    phosphor: string
    background: string
    bezel: string
    rings: number[]
    ownshipCircle: number
    tickEveryDeg: number
    wingMarks: boolean
    legends: Legend[]
    countermeasures: { chaff: number; flares: number } | null
    maxSymbols: number
  }
  placement: { mode: 'Lethality' | 'Amplitude'; critical: number; lethal: number; nonLethal: number }
  threats: ThreatRule[]
  unknown: ThreatRule
  symbols: {
    byClass: Partial<Record<ThreatClass, SymbolShape>>
    priority: SymbolShape
    lock: SymbolShape
    lockMinHitsPerSecond: number
    newThreatMs: number
  }
}

export function resolveProfile(p: SymbologyProfile): ResolvedProfile {
  const d = p.display
  return {
    id: p.id,
    name: p.name,
    platform: p.platform ?? 'FixedWing',
    display: {
      phosphor: d.phosphor,
      background: d.background,
      bezel: d.bezel ?? '#15171a',
      rings: d.rings,
      ownshipCircle: d.ownshipCircle ?? 0.17,
      tickEveryDeg: d.tickEveryDeg ?? 30,
      wingMarks: d.wingMarks ?? true,
      legends: d.legends ?? [],
      countermeasures: d.countermeasures ?? null,
      maxSymbols: d.maxSymbols ?? 16,
    },
    placement: {
      mode: p.placement.mode ?? 'Lethality',
      critical: p.placement.critical ?? 0.3,
      lethal: p.placement.lethal ?? 0.58,
      nonLethal: p.placement.nonLethal ?? 0.86,
    },
    threats: p.threats,
    unknown: p.unknown,
    symbols: {
      byClass: p.symbols.byClass as Partial<Record<ThreatClass, SymbolShape>>,
      priority: p.symbols.priority ?? 'Diamond',
      lock: p.symbols.lock ?? 'Circle',
      lockMinHitsPerSecond: p.symbols.lockMinHitsPerSecond ?? 12,
      newThreatMs: p.symbols.newThreatMs ?? 2500,
    },
  }
}

const inRange = (value: number, range: number[] | null | undefined) => !range || (value >= range[0] && value <= range[1])

/**
 * The first matching rule of the profile's threat library. A rule with emitter ids matches only
 * those emitters; every range a rule gives must contain the measured value.
 */
export function classify(e: Pick<ActiveEmitter, 'emitterId' | 'frequencyMHz' | 'priUs' | 'pulseWidthUs'>, profile: ResolvedProfile): ThreatRule {
  for (const rule of profile.threats) {
    if (rule.emitterIds && rule.emitterIds.length > 0 && !rule.emitterIds.includes(e.emitterId)) continue
    if (inRange(e.frequencyMHz, rule.frequencyMHz) && inRange(e.priUs, rule.priUs) && inRange(e.pulseWidthUs, rule.pulseWidthUs)) return rule
  }
  return profile.unknown
}

export interface ThreatSymbol {
  emitterId: number
  code: string
  threatClass: ThreatClass
  lethality: Lethality
  description: string | null
  bearingDeg: number
  /** Distance from the centre as a fraction of the scope radius. */
  radius: number
  locked: boolean
  isNew: boolean
  /** The single most dangerous threat on the scope. */
  priority: boolean
  /** Decorations to draw around the code, in order. */
  shapes: SymbolShape[]
  amplitudeDb: number
  ageMs: number
}

const LETHALITY_RANK: Record<Lethality, number> = { Critical: 0, Lethal: 1, NonLethal: 2 }

/**
 * What the RWR shows: emitters classified by the profile, ordered by priority (lethality, then
 * tracking, then signal strength), limited to the profile's symbol count, placed and decorated.
 */
export function buildThreats(emitters: readonly ActiveEmitter[], profile: ResolvedProfile): ThreatSymbol[] {
  const { placement, symbols, display } = profile
  const classified = emitters.map((e) => {
    const rule = classify(e, profile)
    return { e, rule, lethality: rule.lethality ?? 'NonLethal', locked: e.hitsPerSecond >= symbols.lockMinHitsPerSecond }
  })
  classified.sort(
    (a, b) =>
      LETHALITY_RANK[a.lethality] - LETHALITY_RANK[b.lethality] ||
      Number(b.locked) - Number(a.locked) ||
      b.e.amplitudeDb - a.e.amplitudeDb ||
      a.e.emitterId - b.e.emitterId,
  )

  const inner = display.ownshipCircle + 0.06
  return classified.slice(0, display.maxSymbols).map(({ e, rule, lethality, locked }, index) => {
    const radius =
      placement.mode === 'Lethality'
        ? lethality === 'Critical'
          ? placement.critical
          : lethality === 'Lethal'
            ? placement.lethal
            : placement.nonLethal
        : inner + (0.94 - inner) * ((radiusFraction(e.amplitudeDb) - 0.14) / 0.86)
    const priority = index === 0
    const shapes: SymbolShape[] = []
    const classShape = symbols.byClass[rule.class]
    if (classShape && classShape !== 'None') shapes.push(classShape)
    if (locked && symbols.lock !== 'None') shapes.push(symbols.lock)
    if (priority && symbols.priority !== 'None') shapes.push(symbols.priority)
    return {
      emitterId: e.emitterId,
      code: rule.code,
      threatClass: rule.class,
      lethality,
      description: rule.description ?? null,
      bearingDeg: e.bearingDeg,
      radius,
      locked,
      isNew: e.presentForMs < symbols.newThreatMs,
      priority,
      shapes,
      amplitudeDb: e.amplitudeDb,
      ageMs: e.ageMs,
    }
  })
}

/** Countermeasures dispensed up to a flight time, from the flight's release marks (sorted by time). */
export function dispensedBy(marks: readonly { t: number; kind: number; count: number }[], positionMs: number) {
  let chaff = 0
  let flares = 0
  for (const m of marks) {
    if (m.t > positionMs) break
    if (m.kind === 2) flares += m.count
    else chaff += m.count
  }
  return { chaff, flares }
}
