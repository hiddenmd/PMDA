import { describe, expect, it } from 'vitest'
import type { Alert } from '../../api/types'
import { ALERTS, AlertTracker, alertsCrossed, clockPosition, lastAlertIndex, loudest } from './alerts'

const alert = (offsetMs: number, kind: Alert['kind'] = 'NewThreat', bearingDeg: number | null = null): Alert => ({
  offsetMs,
  kind,
  emitterId: null,
  bearingDeg,
})

describe('alerts', () => {
  it('calls bearings as clock positions', () => {
    expect(clockPosition(0)).toBe("12 o'clock")
    expect(clockPosition(14)).toBe("12 o'clock")
    expect(clockPosition(16)).toBe("1 o'clock")
    expect(clockPosition(90)).toBe("3 o'clock")
    expect(clockPosition(185)).toBe("6 o'clock")
    expect(clockPosition(350)).toBe("12 o'clock")
    expect(clockPosition(-90)).toBe("9 o'clock")
  })

  it('finds the alerts the playhead crossed, excluding the start and including the end', () => {
    const alerts = [alert(100), alert(200), alert(200), alert(300)]
    expect(alertsCrossed(alerts, 100, 200).map((a) => a.offsetMs)).toEqual([200, 200])
    expect(alertsCrossed(alerts, 0, 1000)).toHaveLength(4)
    expect(alertsCrossed(alerts, 300, 400)).toEqual([])
    expect(alertsCrossed(alerts, 250, 100)).toEqual([]) // backwards (a seek) sounds nothing
  })

  it('finds the last alert at or before the playhead', () => {
    const alerts = [alert(100), alert(200)]
    expect(lastAlertIndex(alerts, 50)).toBe(-1)
    expect(lastAlertIndex(alerts, 100)).toBe(0)
    expect(lastAlertIndex(alerts, 199)).toBe(0)
    expect(lastAlertIndex(alerts, 5000)).toBe(1)
  })

  it('sounds the most severe of simultaneous alerts', () => {
    expect(loudest([alert(1, 'NewThreat'), alert(1, 'MissileLaunch'), alert(1, 'ChaffLow')])?.kind).toBe('MissileLaunch')
    expect(loudest([])).toBeUndefined()
  })

  it('words voice warnings with the clock position when there is a bearing', () => {
    expect(ALERTS.MissileApproach.voice!(alert(0, 'MissileApproach', 95))).toBe("Missile, missile, 3 o'clock")
    expect(ALERTS.MissileLaunch.voice!(alert(0, 'MissileLaunch'))).toBe('Missile launch')
    expect(ALERTS.NewThreat.voice).toBeUndefined()
  })

  describe('alert tracker (what sounds as playback runs)', () => {
    const alerts = [alert(5_000, 'RadarLock'), alert(9_000, 'MissileLaunch')]

    it('sounds the alert the analyst clicked when playback starts on it', () => {
      // Clicking a row seeks exactly to the alert's time, paused; then Play.
      const tracker = new AlertTracker(0)
      expect(tracker.step(5_000, false, 1, alerts)).toBeUndefined() // the seek itself is silent
      expect(tracker.step(5_016, true, 1, alerts)?.offsetMs).toBe(5_000)
      expect(tracker.step(5_033, true, 1, alerts)).toBeUndefined() // and only once
    })

    it('sounds the clicked alert when the seek happens during playback', () => {
      const tracker = new AlertTracker(1_000)
      tracker.step(1_016, true, 1, alerts)
      expect(tracker.step(9_000, true, 1, alerts)).toBeUndefined() // the jump is a seek: silent
      expect(tracker.step(9_016, true, 1, alerts)?.offsetMs).toBe(9_000)
    })

    it('sounds alerts crossed in normal playback, and none skipped by a seek', () => {
      const tracker = new AlertTracker(4_990)
      expect(tracker.step(5_006, true, 1, alerts)?.offsetMs).toBe(5_000)
      expect(tracker.step(8_000, true, 1, alerts)).toBeUndefined() // jumped over nothing it should call
      expect(tracker.step(8_500, false, 1, alerts)).toBeUndefined()
    })

    it('stays silent above 10x and keeps only critical tones at 10x', () => {
      const fast = new AlertTracker(4_990)
      expect(fast.step(5_500, true, 60, alerts)).toBeUndefined()
      const tenX = new AlertTracker(4_990)
      expect(tenX.step(5_150, true, 10, alerts)).toBeUndefined() // a lock is a warning, not critical
      expect(tenX.step(9_100, true, 10, alerts)?.kind).toBe('MissileLaunch') // a 3.95 s step at 10× is playback, not a seek
    })
  })
})
