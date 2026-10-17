import type { DockviewApi } from 'dockview-react'
import { useMemo, useState } from 'react'
import { useAuth } from './auth/authContext'
import { UserMenu } from './auth/UserMenu'
import { useLiveFlightRefresh } from './hooks/useFlights'
import { AppBar } from './workspace/AppBar'
import { Dock } from './workspace/Dock'
import { layoutStore } from './workspace/layouts'
import { PlaybackBar } from './workspace/PlaybackBar'
import { WorkspaceProvider } from './workspace/WorkspaceProvider'

export function App() {
  const { user } = useAuth()
  const [api, setApi] = useState<DockviewApi | null>(null)
  // Layouts are per user.
  const store = useMemo(() => layoutStore(user.userName), [user.userName])
  useLiveFlightRefresh()

  return (
    <WorkspaceProvider>
      <div className="shell">
        <AppBar api={api} store={store} userMenu={<UserMenu api={api} />} />
        <Dock store={store} onReady={setApi} />
        <PlaybackBar />
      </div>
    </WorkspaceProvider>
  )
}
