import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo, type ReactNode } from 'react'
import { getAuthState, getCurrentUser, logout } from '../api/auth'
import { databaseKeys, getDatabaseStatus } from '../api/database'
import { AuthContext, authKeys, type AuthValue } from './authContext'
import { DatabaseSetupScreen } from './DatabaseSetupScreen'
import { LoginScreen } from './LoginScreen'
import { SetupScreen } from './SetupScreen'

/**
 * Shows the app only with a working database and a valid session. Without a database: the database setup
 * screen. Otherwise the login screen, or first-run setup when there are no users yet. A 401 anywhere in the app clears the session (see main.tsx), which brings
 * the login screen back.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const db = useQuery({ queryKey: databaseKeys.status, queryFn: ({ signal }) => getDatabaseStatus(signal), retry: false, staleTime: Infinity })
  const dbReady = db.data?.state === 'Ready'
  const me = useQuery({ queryKey: authKeys.me, queryFn: ({ signal }) => getCurrentUser(signal), retry: false, staleTime: Infinity, enabled: dbReady })
  const signedOut = me.data === null
  const state = useQuery({ queryKey: authKeys.state, queryFn: ({ signal }) => getAuthState(signal), enabled: signedOut, retry: false })

  const signOut = useCallback(async () => {
    await logout()
    // First flip the session (the gate shows the login screen), then forget the previous user's data.
    // Clearing the whole cache first would detach the gate from its session query.
    queryClient.setQueryData(authKeys.me, null)
    queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'auth' })
  }, [queryClient])

  const value = useMemo<AuthValue | null>(
    () => (me.data ? { user: me.data, isAdmin: me.data.role === 'Admin', signOut } : null),
    [me.data, signOut],
  )

  if (db.isPending) return <div className="empty" style={{ height: '100%' }}>Loading…</div>
  if (db.isError) return <div className="empty" style={{ height: '100%' }}>Cannot reach the PMDA server. Retrying shortly…</div>
  if (!dbReady) return <DatabaseSetupScreen status={db.data} />
  if (me.isPending || (signedOut && state.isPending)) return <div className="empty" style={{ height: '100%' }}>Loading…</div>
  if (me.isError) return <div className="empty" style={{ height: '100%' }}>Cannot reach the PMDA server. Retrying shortly…</div>
  if (!value) return state.data?.setupRequired ? <SetupScreen allowed={state.data.setupAllowed} /> : <LoginScreen />

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
