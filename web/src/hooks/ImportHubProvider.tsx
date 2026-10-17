import { useActorRef, useSelector } from '@xstate/react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { createImportHub, type ImportHub } from '../api/importHub'
import { connectionMachine, connectionStatus } from '../machines/connectionMachine'
import { ImportHubContext } from './useImportHub'

/** Owns the single hub connection for the app and keeps it alive through the connection machine. */
export function ImportHubProvider({ children, hub: providedHub }: { children: ReactNode; hub?: ImportHub }) {
  const [hub] = useState(() => providedHub ?? createImportHub())
  const connection = useActorRef(connectionMachine, { input: { hub } })
  const status = useSelector(connection, connectionStatus)

  useEffect(() => {
    connection.send({ type: 'CONNECT' })
    return () => connection.send({ type: 'DISCONNECT' })
  }, [connection])

  const value = useMemo(() => ({ hub, status }), [hub, status])
  return <ImportHubContext.Provider value={value}>{children}</ImportHubContext.Provider>
}
