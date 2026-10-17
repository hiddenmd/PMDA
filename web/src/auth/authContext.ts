import { createContext, useContext } from 'react'
import type { CurrentUser } from '../api/auth'

export interface AuthValue {
  user: CurrentUser
  isAdmin: boolean
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthValue | null>(null)

/** The signed-in user. Only available inside the authenticated part of the app. */
export function useAuth(): AuthValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside <AuthGate>.')
  return value
}

export const authKeys = {
  me: ['auth', 'me'] as const,
  state: ['auth', 'state'] as const,
}
