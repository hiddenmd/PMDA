import { AlertTriangle } from 'lucide-react'
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

  return (
    <div className="playbar" role="region" aria-label="Playback">
      <TransportBar status={p.status} speed={p.speed} positionMs={p.positionMs} startUtc={startUtc} onToggle={p.toggle} onStep={p.step} onSpeed={p.setSpeed}>
        {p.error && (
          <span className="transport__status" role="status">
            <AlertTriangle size={14} aria-hidden /> {p.error}
          </span>
        )}
      </TransportBar>
      <Timeline overview={overview.data} positionMs={p.positionMs} durationMs={durationMs} startUtc={startUtc} onSeek={p.seek} />
    </div>
  )
}
