import { useQuery } from '@tanstack/react-query'
import { Crosshair, Maximize } from 'lucide-react'
import { useRef, useState } from 'react'
import { getMapSettings, getTrack } from '../../api/playback'
import { useAlerts } from '../../features/alerts/useAlerts'
import { MapView, type MapReadout } from '../../features/map/MapView'
import { useSymbology } from '../../features/rwr/useSymbology'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

/** About one position per screen pixel of a wide panel; the server averages within each slice. */
const TRACK_POINTS = 4_000

export function MapPanel() {
  const { playback, flight, flightLoading } = useWorkspace()
  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <FlightMap flightId={flight.id} actor={playback.actor} startUtc={playback.startUtc} />
}

function FlightMap({ flightId, actor, startUtc }: { flightId: string; actor: PlaybackActor; startUtc: string }) {
  const settings = useQuery({ queryKey: ['map', 'settings'], queryFn: ({ signal }) => getMapSettings(signal), staleTime: Infinity })
  const track = useQuery({
    queryKey: ['flights', 'track', flightId, TRACK_POINTS],
    queryFn: ({ signal }) => getTrack(flightId, TRACK_POINTS, signal),
    staleTime: Infinity, // a completed flight never changes
  })
  const alerts = useAlerts(flightId)
  const { profile } = useSymbology()
  const [follow, setFollow] = useState(true)
  const [fitRequest, setFitRequest] = useState(0)
  const readout = useRef<MapReadout>({ lat: null, lon: null, alt: null, heading: null, speed: null })

  const error = settings.error ?? track.error
  const ready = settings.data && track.data && (alerts.data || alerts.isError)

  return (
    <div className="map-panel">
      <div className="panel-toolbar">
        <button type="button" className={`btn btn--compact ${follow ? 'btn--primary' : 'btn--secondary'}`} aria-pressed={follow} onClick={() => setFollow((f) => !f)}>
          <Crosshair size={14} aria-hidden /> Follow aircraft
        </button>
        <button
          type="button"
          className="btn btn--secondary btn--compact"
          onClick={() => {
            setFollow(false)
            setFitRequest((n) => n + 1)
          }}
        >
          <Maximize size={14} aria-hidden /> Whole track
        </button>
        <span className="panel-toolbar__spacer" />
        <span className="map-legend small muted" aria-hidden>
          <span className="map-legend__missile" /> Missile alert
          <span className="map-legend__line map-legend__line--lock" /> Tracking radar
          <span className="map-legend__line map-legend__line--critical" /> Critical threat
        </span>
      </div>

      {error ? (
        <div className="empty">Could not load the map: {error.message}</div>
      ) : !ready ? (
        <div className="empty">Loading map…</div>
      ) : track.data!.t.length === 0 ? (
        <div className="empty">This flight has no navigation data, so there is no track to show.</div>
      ) : (
        <MapView
          actor={actor}
          track={track.data!}
          alerts={alerts.data ?? []}
          settings={settings.data!}
          profile={profile}
          startUtc={startUtc}
          follow={follow}
          fitRequest={fitRequest}
          onFollowChange={setFollow}
          readout={readout}
        />
      )}

      <dl className="readout readout--map" aria-label="Aircraft at the playhead">
        <div>
          <dt>Latitude</dt>
          <dd ref={(el) => void (readout.current.lat = el)}>—</dd>
        </div>
        <div>
          <dt>Longitude</dt>
          <dd ref={(el) => void (readout.current.lon = el)}>—</dd>
        </div>
        <div>
          <dt>Altitude</dt>
          <dd ref={(el) => void (readout.current.alt = el)}>—</dd>
        </div>
        <div>
          <dt>Track</dt>
          <dd ref={(el) => void (readout.current.heading = el)}>—</dd>
        </div>
        <div>
          <dt>Ground speed</dt>
          <dd ref={(el) => void (readout.current.speed = el)}>—</dd>
        </div>
      </dl>
    </div>
  )
}
