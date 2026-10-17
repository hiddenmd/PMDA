import { createContext, useContext } from 'react'
import type { ImportHub } from '../api/importHub'
import type { ConnectionStatus } from '../machines/connectionMachine'

export interface ImportHubValue {
  hub: ImportHub
  status: ConnectionStatus
}

export const ImportHubContext = createContext<ImportHubValue | null>(null)

/** The app's hub connection and its status. Requires <ImportHubProvider>. */
export function useImportHub(): ImportHubValue {
  const value = useContext(ImportHubContext)
  if (!value) throw new Error('useImportHub must be used inside <ImportHubProvider>.')
  return value
}
