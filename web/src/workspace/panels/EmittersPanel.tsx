import { useMemo } from 'react'
import { EmitterTable } from '../../features/playback/EmitterTable'
import { usePlaybackState } from '../../features/playback/usePlayback'
import { buildThreats } from '../../features/rwr/symbology'
import { useSymbology } from '../../features/rwr/useSymbology'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

export function EmittersPanel() {
  const { playback, flightLoading } = useWorkspace()
  if (!playback) return <NoPlayback loading={flightLoading} />
  return <EmittersView actor={playback.actor} />
}

function EmittersView({ actor }: { actor: PlaybackActor }) {
  const { view } = usePlaybackState(actor)
  const { profile } = useSymbology()
  const rows = useMemo(() => {
    if (!profile) return []
    // Same classification and order as the scope, but every emitter (the scope limits its symbols).
    const all = buildThreats(view.emitters, { ...profile, display: { ...profile.display, maxSymbols: Number.MAX_SAFE_INTEGER } })
    const byId = new Map(view.emitters.map((e) => [e.emitterId, e]))
    return all.map((threat) => ({ threat, emitter: byId.get(threat.emitterId)! }))
  }, [view.emitters, profile])

  return (
    <div className="panel-body panel-body--flush">
      <EmitterTable rows={rows} persistenceMs={view.persistenceMs} />
    </div>
  )
}
