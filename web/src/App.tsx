import { Radar } from 'lucide-react'
import { useState } from 'react'
import { ConnectionBadge } from './components/ConnectionBadge'
import { ThemeToggle } from './components/ThemeToggle'
import { FlightList } from './features/flights/FlightList'
import { FlightWorkspace } from './features/flights/FlightWorkspace'
import { ImportPanel } from './features/imports/ImportPanel'
import { useLiveFlightRefresh } from './hooks/useFlights'
import { useImportHub } from './hooks/useImportHub'

export function App() {
  const { status } = useImportHub()
  const [selectedFlightId, setSelectedFlightId] = useState<string | null>(null)
  useLiveFlightRefresh()

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <Radar size={22} aria-hidden />
          <h1>
            PMDA <span className="brand__sub">Post-Mission Data Analysis</span>
          </h1>
        </div>
        <div className="topbar__spacer" />
        <ConnectionBadge status={status} />
        <ThemeToggle />
      </header>
      <aside className="rail" aria-label="Import and flights">
        <ImportPanel onOpenFlight={setSelectedFlightId} />
        <FlightList selectedId={selectedFlightId} onSelect={setSelectedFlightId} />
      </aside>
      <main className="workspace">
        <FlightWorkspace flightId={selectedFlightId} />
      </main>
    </div>
  )
}
