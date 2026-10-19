import { useMemo, useState } from 'react'
import { storage } from '../../lib/preferences'
import { usePlaybackState } from '../../features/playback/usePlayback'
import { RwrScope, type RwrLook } from '../../features/rwr/RwrScope'
import { buildThreats } from '../../features/rwr/symbology'
import { useReleases, useSymbology } from '../../features/rwr/useSymbology'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

const LOOK_KEY = 'pmda-rwr-look'
const LOOKS: { value: RwrLook; label: string }[] = [
  { value: 'cockpit', label: 'Cockpit' },
  { value: 'tactical', label: 'Tactical' },
]

export function ScopePanel() {
  const { playback, flight, flightLoading } = useWorkspace()
  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <ScopeView actor={playback.actor} flightId={flight.id} customerId={flight.customerId} />
}

function ScopeView({ actor, flightId, customerId }: { actor: PlaybackActor; flightId: string; customerId: string }) {
  const { view } = usePlaybackState(actor)
  const { profiles, profile, choice, choose, error } = useSymbology()
  const releases = useReleases(flightId)
  const threats = useMemo(() => (profile ? buildThreats(view.emitters, profile) : []), [view.emitters, profile])
  const { position } = view
  const [look, setLook] = useState<RwrLook>(() => (storage.get(LOOK_KEY) === 'tactical' ? 'tactical' : 'cockpit'))
  const chooseLook = (next: RwrLook) => {
    setLook(next)
    storage.set(LOOK_KEY, next)
  }

  const label =
    threats.length === 0
      ? 'RWR: no threats at this moment'
      : `RWR: ${threats.length} threat${threats.length === 1 ? '' : 's'}; priority ${threats[0].code} (${threats[0].threatClass}) at ${threats[0].bearingDeg.toFixed(0)} degrees${threats[0].locked ? ', tracking' : ''}`

  return (
    <div className="scope-panel">
      <div className="panel-toolbar" style={{ margin: 'calc(-1 * var(--space-md)) calc(-1 * var(--space-md)) 0' }}>
        <label className="small muted" htmlFor={`symbology-${flightId}`}>
          Symbology
        </label>
        <select id={`symbology-${flightId}`} className="select select--compact" value={choice ?? ''} onChange={(e) => choose(e.target.value || null)}>
          <option value="">Auto: {customerId}'s profile</option>
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        {profile && <span className="muted small">{profile.name}</span>}
        <span className="panel-toolbar__spacer" />
        <div className="segmented" role="group" aria-label="RWR display style">
          {LOOKS.map((l) => (
            <button key={l.value} type="button" aria-pressed={look === l.value} onClick={() => chooseLook(l.value)}>
              {l.label}
            </button>
          ))}
        </div>
      </div>
      {error && <div className="empty">Could not load symbology.</div>}
      {profile ? <RwrScope actor={actor} profile={profile} look={look} releases={releases.data ?? []} label={label} /> : !error && <div className="empty">Loading symbology…</div>}
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
