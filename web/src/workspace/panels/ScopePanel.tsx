import { Scope } from '../../features/playback/Scope'
import { usePlaybackState } from '../../features/playback/usePlayback'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

export function ScopePanel() {
  const { playback, flightLoading } = useWorkspace()
  if (!playback) return <NoPlayback loading={flightLoading} />
  return <ScopeView actor={playback.actor} />
}

function ScopeView({ actor }: { actor: PlaybackActor }) {
  const { view } = usePlaybackState(actor)
  const { emitters, position } = view
  const label =
    emitters.length === 0
      ? 'RWR scope: no emitters at this moment'
      : `RWR scope: ${emitters.length} emitter${emitters.length === 1 ? '' : 's'}, strongest ${emitters[0].emitterId} at ${emitters[0].bearingDeg.toFixed(0)} degrees`

  return (
    <div className="scope-panel">
      <Scope actor={actor} label={label} />
      <dl className="readout" aria-label="Aircraft position">
        <div>
          <dt>Latitude</dt>
          <dd>{position ? position.lat.toFixed(4) : '—'}</dd>
        </div>
        <div>
          <dt>Longitude</dt>
          <dd>{position ? position.lon.toFixed(4) : '—'}</dd>
        </div>
        <div>
          <dt>Altitude</dt>
          <dd>{position ? `${Math.round(position.altM)} m` : '—'}</dd>
        </div>
      </dl>
    </div>
  )
}
