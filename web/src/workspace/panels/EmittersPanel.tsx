import { EmitterTable } from '../../features/playback/EmitterTable'
import { usePlaybackState } from '../../features/playback/usePlayback'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

export function EmittersPanel() {
  const { playback, flightLoading } = useWorkspace()
  if (!playback) return <NoPlayback loading={flightLoading} />
  return <EmittersView actor={playback.actor} />
}

function EmittersView({ actor }: { actor: PlaybackActor }) {
  const { view } = usePlaybackState(actor)
  return (
    <div className="panel-body panel-body--flush">
      <EmitterTable emitters={view.emitters} persistenceMs={view.persistenceMs} />
    </div>
  )
}
