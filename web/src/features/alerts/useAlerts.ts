import { useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useState } from 'react'
import { getAlerts } from '../../api/playback'
import type { Alert } from '../../api/types'
import { storage } from '../../lib/preferences'
import { transportStatus, MAX_TICK_MS } from '../../machines/playbackMachine'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { ALERTS, alertsCrossed, loudest } from './alerts'
import { AlertSound } from './alertSound'

export function useAlerts(flightId: string) {
  return useQuery({
    queryKey: ['flights', 'alerts', flightId],
    queryFn: ({ signal }) => getAlerts(flightId, signal),
    staleTime: Infinity, // a completed flight never changes
  })
}

const SOUND_KEY = 'pmda-alert-sound'

/** Whether alerts sound during playback; on unless the analyst turned it off. */
export function useSoundPreference() {
  const [enabled, setEnabled] = useState(() => storage.get(SOUND_KEY) !== 'off')
  const toggle = useCallback(() => {
    setEnabled((on) => {
      storage.set(SOUND_KEY, on ? 'off' : 'on')
      return !on
    })
  }, [])
  return { enabled, toggle }
}

/**
 * Sounds the alerts the playhead crosses while playing, as the crew heard them: tones and voice at
 * 1×, only critical tones at 10×, nothing faster (it would be noise). Seeks sound nothing. Runs on
 * actor updates, outside React rendering.
 */
export function useAlertSound(actor: PlaybackActor, alerts: readonly Alert[] | undefined, enabled: boolean) {
  useEffect(() => {
    if (!enabled || !alerts?.length) return
    const sound = new AlertSound()
    let last = actor.getSnapshot().context.positionMs
    const subscription = actor.subscribe((snapshot) => {
      const { positionMs, speed } = snapshot.context
      const from = last
      last = positionMs
      if (transportStatus(snapshot) !== 'playing' || speed > 10) return
      if (positionMs - from > MAX_TICK_MS * speed) return // a jump: a seek, not playback

      const crossed = alertsCrossed(alerts, from, positionMs)
      const alert = loudest(speed === 1 ? crossed : crossed.filter((a) => ALERTS[a.kind].severity === 'critical'))
      if (alert) sound.play(alert, speed === 1)
    })
    return () => {
      subscription.unsubscribe()
      sound.dispose()
    }
  }, [actor, alerts, enabled])
}
