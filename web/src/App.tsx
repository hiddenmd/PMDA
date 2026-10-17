import type { DockviewApi } from 'dockview-react'
import { useMemo, useState, type ReactNode } from 'react'
import { useLiveFlightRefresh } from './hooks/useFlights'
import { AppBar } from './workspace/AppBar'
import { Dock } from './workspace/Dock'
import { layoutStore } from './workspace/layouts'
import { PlaybackBar } from './workspace/PlaybackBar'
import { WorkspaceProvider } from './workspace/WorkspaceProvider'

interface AppProps {
  /** Identifies the user for per-user layouts. */
  userKey?: string
  userMenu?: ReactNode
}

export function App({ userKey = 'local', userMenu }: AppProps) {
  const [api, setApi] = useState<DockviewApi | null>(null)
  const store = useMemo(() => layoutStore(userKey), [userKey])
  useLiveFlightRefresh()

  return (
    <WorkspaceProvider>
      <div className="shell">
        <AppBar api={api} store={store} userMenu={userMenu} />
        <Dock store={store} onReady={setApi} />
        <PlaybackBar />
      </div>
    </WorkspaceProvider>
  )
}
