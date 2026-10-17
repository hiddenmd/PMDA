import { AlertTriangle } from 'lucide-react'
import { CountermeasureLog } from './CountermeasureLog'
import { EmitterTable } from './EmitterTable'
import { Scope } from './Scope'
import { Timeline } from './Timeline'
import { TransportBar } from './TransportBar'
import { usePlayback } from './usePlayback'

interface PlaybackPanelProps {
  flightId: string
  startUtc: string
  durationMs: number
}

/** Debrief replay of one flight. Keyed by flight id by the parent, so each flight gets a fresh machine. */
export function PlaybackPanel({ flightId, startUtc, durationMs }: PlaybackPanelProps) {
  const p = usePlayback(flightId, durationMs)
  const { emitters, countermeasures, position, persistenceMs } = p.view
  const scopeLabel =
    emitters.length === 0
      ? 'RWR scope: no emitters at this moment'
      : `RWR scope: ${emitters.length} emitter${emitters.length === 1 ? '' : 's'}, strongest ${emitters[0].emitterId} at ${emitters[0].bearingDeg.toFixed(0)} degrees`

  return (
    <section className="panel" aria-labelledby="playback-title">
      <div className="panel__header">
        <h2 id="playback-title">Debrief playback</h2>
        {p.error && (
          <span className="transport__status" role="status">
            <AlertTriangle size={14} aria-hidden /> {p.error}
          </span>
        )}
      </div>

      <div className="playback">
        <div className="playback__scope">
          <h3>RWR scope</h3>
          <Scope actor={p.actor} label={scopeLabel} />
          <dl className="readout" aria-label="Aircraft position">
            <div>
              <dt>Lat</dt>
              <dd>{position ? position.lat.toFixed(4) : '—'}</dd>
            </div>
            <div>
              <dt>Lon</dt>
              <dd>{position ? position.lon.toFixed(4) : '—'}</dd>
            </div>
            <div>
              <dt>Alt</dt>
              <dd>{position ? `${Math.round(position.altM)} m` : '—'}</dd>
            </div>
          </dl>
        </div>

        <div className="playback__side">
          <div>
            <h3>Active emitters ({emitters.length})</h3>
            <EmitterTable emitters={emitters} persistenceMs={persistenceMs} />
          </div>
          <div>
            <h3>Countermeasures</h3>
            <CountermeasureLog entries={countermeasures} />
          </div>
        </div>

        <div className="playback__transport">
          <TransportBar
            status={p.status}
            speed={p.speed}
            positionMs={p.positionMs}
            startUtc={startUtc}
            onToggle={p.toggle}
            onStep={p.step}
            onSpeed={p.setSpeed}
          />
          <Timeline overview={p.overview} positionMs={p.positionMs} durationMs={durationMs} startUtc={startUtc} onSeek={p.seek} />
        </div>
      </div>
    </section>
  )
}
