import { describe, expect, it } from 'vitest'
import type { Alert } from '../../api/types'
import { ALERTS, alertsCrossed, clockPosition, lastAlertIndex, loudest } from './alerts'

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
})
