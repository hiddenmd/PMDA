import { useQuery } from '@tanstack/react-query'
import { Info } from 'lucide-react'
import { useMemo, useRef } from 'react'
import { getTrack } from '../../api/playback'
import { deriveAttitude, type Attitude } from '../../features/attitude/attitude'
import { AttitudeView, type AttitudeReadout } from '../../features/attitude/AttitudeView'
import { useSymbology } from '../../features/rwr/useSymbology'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

/** Fine enough for rates: a position about every 200 ms (the server averages within each slice). */
const SLICE_MS = 200
const MAX_POINTS = 20_000

const FIELDS: { key: keyof Attitude; label: string }[] = [
  { key: 'pitch', label: 'Pitch' },
  { key: 'roll', label: 'Bank' },
  { key: 'heading', label: 'Track' },
  { key: 'rollRate', label: 'Roll rate' },
  { key: 'pitchRate', label: 'Pitch rate' },
  { key: 'turnRate', label: 'Turn rate' },
  { key: 'verticalSpeed', label: 'Vertical speed' },
  { key: 'groundSpeed', label: 'Ground speed' },
  { key: 'loadFactor', label: 'Load factor' },
]

export function AttitudePanel() {
  const { playback, flight, flightLoading } = useWorkspace()
  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <AttitudeDisplay flightId={flight.id} actor={playback.actor} durationMs={playback.durationMs} />
}

function AttitudeDisplay({ flightId, actor, durationMs }: { flightId: string; actor: PlaybackActor; durationMs: number }) {
  const points = Math.max(2, Math.min(MAX_POINTS, Math.round(durationMs / SLICE_MS)))
  const track = useQuery({
    queryKey: ['flights', 'track', flightId, points],
    queryFn: ({ signal }) => getTrack(flightId, points, signal),
    staleTime: Infinity, // a completed flight never changes
  })
  const series = useMemo(() => (track.data ? deriveAttitude(track.data) : null), [track.data])
  const { profile } = useSymbology()
  const readout = useRef<AttitudeReadout>({})

  return (
    <div className="attitude-panel">
      <p className="attitude-panel__source small muted" title="Recordings with attitude from the aircraft's navigation system will show recorded values instead.">
        <Info size={13} aria-hidden /> Derived from the navigation track: bank from turn rate (coordinated turn), pitch from climb angle. This recording has no
        attitude data.
      </p>

      {track.isError ? (
        <div className="empty">Could not load the track: {track.error.message}</div>
      ) : !series ? (
        <div className="empty">Loading attitude…</div>
      ) : series.t.length < 2 ? (
        <div className="empty">This flight has no navigation data, so attitude cannot be shown.</div>
      ) : (
        <AttitudeView actor={actor} series={series} platform={profile?.platform ?? 'FixedWing'} readout={readout} />
      )}

      <dl className="readout readout--attitude" aria-label="Attitude and rates at the playhead">
        {FIELDS.map((f) => (
          <div key={f.key}>
            <dt>{f.label}</dt>
            <dd ref={(el) => void (readout.current[f.key] = el)}>—</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
