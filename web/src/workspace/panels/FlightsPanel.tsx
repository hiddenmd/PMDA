import { FlightList } from '../../features/flights/FlightList'
import { ImportPanel } from '../../features/imports/ImportPanel'
import { useAuth } from '../../auth/authContext'
import { useWorkspace } from '../workspaceContext'

export function FlightsPanel() {
  const { flightId, selectFlight } = useWorkspace()
  const { isAdmin } = useAuth()
  return (
    <div className="panel-body">
      <ImportPanel onOpenFlight={selectFlight} />
      <FlightList selectedId={flightId} onSelect={selectFlight} canManage={isAdmin} />
    </div>
  )
}
