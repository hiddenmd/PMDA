import { CountermeasureLog } from '../../features/playback/CountermeasureLog'
import { usePlaybackState } from '../../features/playback/usePlayback'
import { useWorkspace, type PlaybackActor } from '../workspaceContext'
import { NoPlayback } from './EmptyPanel'

export function CountermeasuresPanel() {
  const { playback, flightLoading } = useWorkspace()
  if (!playback) return <NoPlayback loading={flightLoading} />
  return <CountermeasuresView actor={playback.actor} />
}

function CountermeasuresView({ actor }: { actor: PlaybackActor }) {
  const { view } = usePlaybackState(actor)
  return (
    <div className="panel-body">
      <CountermeasureLog entries={view.countermeasures} />
    </div>
  )
}
