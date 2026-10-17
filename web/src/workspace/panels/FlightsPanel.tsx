import { FlightList } from '../../features/flights/FlightList'
import { ImportPanel } from '../../features/imports/ImportPanel'
import { useWorkspace } from '../workspaceContext'

export function FlightsPanel() {
  const { flightId, selectFlight } = useWorkspace()
  return (
    <div className="panel-body">
      <ImportPanel onOpenFlight={selectFlight} />
      <FlightList selectedId={flightId} onSelect={selectFlight} />
    </div>
  )
}
