import type { DockviewApi } from 'dockview-react'
import { LogOut } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useAuth } from './auth/authContext'
import { Menu, MenuItem, MenuLabel } from './components/Menu'
import { useLiveFlightRefresh } from './hooks/useFlights'
import { AppBar } from './workspace/AppBar'
import { Dock } from './workspace/Dock'
import { layoutStore } from './workspace/layouts'
import { PlaybackBar } from './workspace/PlaybackBar'
import { WorkspaceProvider } from './workspace/WorkspaceProvider'

export function App() {
  const { user, signOut } = useAuth()
  const [api, setApi] = useState<DockviewApi | null>(null)
  const store = useMemo(() => layoutStore(user.userName), [user.userName])
  const userMenu = (
    <Menu
      label={`Signed in as ${user.displayName}`}
      trigger={
        <span className="user-button">
          <span className="avatar" aria-hidden>
            {user.displayName.slice(0, 1).toUpperCase()}
          </span>
          {user.displayName}
        </span>
      }
    >
      {() => (
        <>
          <MenuLabel>
            {user.userName} · {user.role}
          </MenuLabel>
          <MenuItem icon={<LogOut size={14} aria-hidden />} onSelect={() => void signOut()}>
            Sign out
          </MenuItem>
        </>
      )}
    </Menu>
  )
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
