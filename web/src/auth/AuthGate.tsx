import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useMemo, type ReactNode } from 'react'
import { getAuthState, getCurrentUser, logout } from '../api/auth'
import { AuthContext, authKeys, type AuthValue } from './authContext'
import { LoginScreen } from './LoginScreen'
import { SetupScreen } from './SetupScreen'

/**
 * Shows the app only with a valid session; otherwise the login screen, or first-run setup when
 * there are no users yet. A 401 anywhere in the app clears the session (see main.tsx), which brings
 * the login screen back.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: authKeys.me, queryFn: ({ signal }) => getCurrentUser(signal), retry: false, staleTime: Infinity })
  const signedOut = me.data === null
  const state = useQuery({ queryKey: authKeys.state, queryFn: ({ signal }) => getAuthState(signal), enabled: signedOut, retry: false })

  const signOut = useCallback(async () => {
    await logout()
    queryClient.clear()
    queryClient.setQueryData(authKeys.me, null)
  }, [queryClient])

  const value = useMemo<AuthValue | null>(
    () => (me.data ? { user: me.data, isAdmin: me.data.role === 'Admin', signOut } : null),
    [me.data, signOut],
  )

  if (me.isPending || (signedOut && state.isPending)) return <div className="empty" style={{ height: '100%' }}>Loading…</div>
  if (me.isError) return <div className="empty" style={{ height: '100%' }}>Cannot reach the PMDA server. Retrying shortly…</div>
  if (!value) return state.data?.setupRequired ? <SetupScreen allowed={state.data.setupAllowed} /> : <LoginScreen />

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
