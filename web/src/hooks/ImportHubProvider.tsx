import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { createActor } from 'xstate'
import { createImportHub, type ImportHub } from '../api/importHub'
import { connectionMachine, connectionStatus, type ConnectionStatus } from '../machines/connectionMachine'
import { ImportHubContext } from './useImportHub'

/**
 * Owns the single hub connection for the app and keeps it alive through the connection machine.
 *
 * The actor is created and stopped inside one effect (not with useActorRef) so the cleanup can
 * send DISCONNECT, which stops the hub, before the actor itself stops. With useActorRef the actor
 * is stopped first and the DISCONNECT is lost, leaking the connection on unmount and breaking the
 * StrictMode remount.
 */
export function ImportHubProvider({ children, hub: providedHub }: { children: ReactNode; hub?: ImportHub }) {
  const [hub] = useState(() => providedHub ?? createImportHub())
  const [status, setStatus] = useState<ConnectionStatus>('connecting')

  useEffect(() => {
    const actor = createActor(connectionMachine, { input: { hub } })
    const subscription = actor.subscribe((snapshot) => setStatus(connectionStatus(snapshot)))
    actor.start()
    actor.send({ type: 'CONNECT' })
    return () => {
      subscription.unsubscribe()
      actor.send({ type: 'DISCONNECT' })
      actor.stop()
    }
  }, [hub])

  const value = useMemo(() => ({ hub, status }), [hub, status])
  return <ImportHubContext.Provider value={value}>{children}</ImportHubContext.Provider>
}
