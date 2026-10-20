import type { Alert, AlertKind } from '../../api/types'
import { MAX_TICK_MS } from '../../machines/playbackMachine'

export type AlertSeverity = 'critical' | 'warning' | 'advisory'

/** How each aural warning sounds, as a pattern of tones; see alertSound.ts. */
export type TonePattern = 'newThreat' | 'lock' | 'launch' | 'approach' | 'chime'

interface AlertSpec {
  label: string
  /** Cockpit-style caption for compact places (timeline, map). */
  caption: string
  severity: AlertSeverity
  tone: TonePattern
  /** What the voice warning says, if the alert has one. */
  voice?: (alert: Alert) => string
}

const withClock = (text: string) => (a: Alert) => (a.bearingDeg === null ? text : `${text}, ${clockPosition(a.bearingDeg)}`)

export const ALERTS: Record<AlertKind, AlertSpec> = {
  Unknown: { label: 'Alert', caption: 'ALERT', severity: 'advisory', tone: 'chime' },
  NewThreat: { label: 'New threat', caption: 'NEW', severity: 'advisory', tone: 'newThreat' },
  RadarLock: { label: 'Radar lock', caption: 'LOCK', severity: 'warning', tone: 'lock' },
  MissileLaunch: { label: 'Missile launch', caption: 'LAUNCH', severity: 'critical', tone: 'launch', voice: withClock('Missile launch') },
  MissileApproach: { label: 'Missile approach', caption: 'MISSILE', severity: 'critical', tone: 'approach', voice: withClock('Missile, missile') },
  ChaffLow: { label: 'Chaff low', caption: 'CHAFF LO', severity: 'warning', tone: 'chime', voice: () => 'Chaff low' },
  FlaresLow: { label: 'Flares low', caption: 'FLARE LO', severity: 'warning', tone: 'chime', voice: () => 'Flares low' },
  ChaffOut: { label: 'Chaff out', caption: 'CHAFF OUT', severity: 'warning', tone: 'chime', voice: () => 'Chaff out' },
  FlaresOut: { label: 'Flares out', caption: 'FLARE OUT', severity: 'warning', tone: 'chime', voice: () => 'Flares out' },
}

export const SEVERITY_RANK: Record<AlertSeverity, number> = { advisory: 0, warning: 1, critical: 2 }

/** Relative bearing as a clock position, the way crews call it: 0° is twelve o'clock, 90° is three. */
export function clockPosition(bearingDeg: number): string {
  const hour = Math.round((((bearingDeg % 360) + 360) % 360) / 30) % 12
  return `${hour === 0 ? 12 : hour} o'clock`
}

/** Index of the first alert after `ms`, in alerts sorted by time. */
function firstAfter(alerts: readonly Alert[], ms: number): number {
  let lo = 0
  let hi = alerts.length
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    if (alerts[mid].offsetMs <= ms) lo = mid + 1
    else hi = mid
  }
  return lo
}

/** Alerts with fromMs < time <= toMs: the ones the playhead crossed moving from fromMs to toMs. */
export function alertsCrossed(alerts: readonly Alert[], fromMs: number, toMs: number): Alert[] {
  if (toMs <= fromMs) return []
  return alerts.slice(firstAfter(alerts, fromMs), firstAfter(alerts, toMs))
}

/** The last alert at or before the playhead, as an index, or -1. */
export function lastAlertIndex(alerts: readonly Alert[], positionMs: number): number {
  return firstAfter(alerts, positionMs) - 1
}

/** Of several alerts at once, the one to sound: the most severe, then the latest. */
export function loudest(alerts: readonly Alert[]): Alert | undefined {
  let best: Alert | undefined
  for (const a of alerts) {
    if (!best || SEVERITY_RANK[ALERTS[a.kind].severity] >= SEVERITY_RANK[ALERTS[best.kind].severity]) best = a
  }
  return best
}

/** AlertKind by its numeric code, as stored in a warning event's subType (see PMDA.Core AlertKind). */
export const ALERT_CODES: readonly AlertKind[] = [
  'Unknown',
  'NewThreat',
  'RadarLock',
  'MissileLaunch',
  'MissileApproach',
  'ChaffLow',
  'FlaresLow',
  'ChaffOut',
  'FlaresOut',
]

export const alertKindOf = (code: number | null | undefined): AlertKind => (code !== null && code !== undefined && ALERT_CODES[code]) || 'Unknown'

/**
 * Follows the playhead and says which alert to sound at each step, as the crew heard them: tones and voice
 * at 1×, only critical tones at 10×, nothing faster. Seeks are silent, but playback that starts (or resumes
 * after a seek or pause) exactly on an alert calls it, so clicking an alert and playing from it sounds it.
 */
export class AlertTracker {
  private last: number
  /** True when the next playback step starts afresh: its window includes the starting instant. */
  private fresh = true

  constructor(positionMs: number) {
    this.last = positionMs
  }

  step(positionMs: number, playing: boolean, speed: number, alerts: readonly Alert[]): Alert | undefined {
    const from = this.last
    this.last = positionMs
    const seek = positionMs < from || positionMs - from > MAX_TICK_MS * speed
    if (!playing || speed > 10 || seek) {
      this.fresh = true
      return undefined
    }
    // Offsets are whole milliseconds: half a millisecond earlier includes an alert exactly at the start.
    const crossed = alertsCrossed(alerts, this.fresh ? from - 0.5 : from, positionMs)
    this.fresh = false
    return loudest(speed === 1 ? crossed : crossed.filter((a) => ALERTS[a.kind].severity === 'critical'))
  }
}
