import { useState } from 'react'
import { ConnectionBadge } from './components/ConnectionBadge'
import { FlightDetails } from './features/flights/FlightDetails'
import { FlightsSection } from './features/flights/FlightsSection'
import { ImportPanel } from './features/imports/ImportPanel'
import { useImportHub } from './hooks/useImportHub'
import { useLiveFlightRefresh } from './hooks/useFlights'

export function App() {
  const { status } = useImportHub()
  const [selectedFlightId, setSelectedFlightId] = useState<string | null>(null)
  useLiveFlightRefresh()

  return (
    <div className="app">
      <header className="app__header">
        <h1>
          PMDA <span className="muted">Post-Mission Data Analysis</span>
        </h1>
        <ConnectionBadge status={status} />
      </header>
      <main className="app__main">
        <div className="app__column">
          <ImportPanel onOpenFlight={setSelectedFlightId} />
          <FlightsSection selectedId={selectedFlightId} onSelect={setSelectedFlightId} />
        </div>
        <FlightDetails flightId={selectedFlightId} />
      </main>
    </div>
  )
}
