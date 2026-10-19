import { AlertTriangle, Volume2, VolumeX } from '../components/icons'
import { useAlerts, useAlertSound, useSoundPreference } from '../features/alerts/useAlerts'
import { Timeline } from '../features/playback/Timeline'
import { TransportBar } from '../features/playback/TransportBar'
import { useFlightOverview, usePlaybackState } from '../features/playback/usePlayback'
import { useWorkspace, type PlaybackActor } from './workspaceContext'

/** Transport controls and the event-density scrubber, pinned under the dock. */
export function PlaybackBar() {
  const { playback, flight } = useWorkspace()
  if (!playback) {
    return (
      <div className="playbar playbar--idle" role="region" aria-label="Playback">
        {flight ? 'Playback is available for completed flights with events.' : 'Select a flight to replay it.'}
      </div>
    )
  }
  return <ActivePlaybackBar actor={playback.actor} durationMs={playback.durationMs} startUtc={playback.startUtc} flightId={flight!.id} />
}

function ActivePlaybackBar({ actor, durationMs, startUtc, flightId }: { actor: PlaybackActor; durationMs: number; startUtc: string; flightId: string }) {
  const p = usePlaybackState(actor)
  const overview = useFlightOverview(flightId)
  const alerts = useAlerts(flightId)
  const sound = useSoundPreference()
  useAlertSound(actor, alerts.data, sound.enabled)

  return (
    <div className="playbar" role="region" aria-label="Playback">
      <TransportBar
        status={p.status}
        speed={p.speed}
        positionMs={p.positionMs}
        durationMs={durationMs}
        startUtc={startUtc}
        onToggle={p.toggle}
        onStep={p.step}
        onSpeed={p.setSpeed}
      >
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          aria-pressed={sound.enabled}
          aria-label="Alert sound"
          title={sound.enabled ? 'Alert sound on: tones and voice at 1×, missile tones at 10×' : 'Alert sound off'}
          onClick={sound.toggle}
        >
          {sound.enabled ? <Volume2 size={16} aria-hidden /> : <VolumeX size={16} aria-hidden />}
        </button>
        {p.error && (
          <span className="transport__status" role="status">
            <AlertTriangle size={14} aria-hidden /> {p.error}
          </span>
        )}
      </TransportBar>
      <Timeline overview={overview.data} alerts={alerts.data} positionMs={p.positionMs} durationMs={durationMs} startUtc={startUtc} onSeek={p.seek} />
    </div>
  )
}
