import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import type { ImportJob } from './types'

type Listener<T extends unknown[] = []> = (...args: T) => void
type Unsubscribe = () => void

/**
 * The /hubs/imports connection. Lifecycle callbacks are fanned out to any number of listeners,
 * each with its own unsubscribe (SignalR itself cannot remove onclose/onreconnecting handlers).
 */
export interface ImportHub {
  start(): Promise<void>
  stop(): Promise<void>
  onImportUpdated(listener: Listener<[ImportJob]>): Unsubscribe
  onReconnecting(listener: Listener): Unsubscribe
  onReconnected(listener: Listener): Unsubscribe
  onClose(listener: Listener): Unsubscribe
}

export const importHubPath = '/hubs/imports'

export function createImportHub(url: string = importHubPath): ImportHub {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(url)
    // Short automatic retries; after these give up, the connection machine takes over with backoff.
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
    .configureLogging(LogLevel.Warning)
    .build()

  const updated = new Set<Listener<[ImportJob]>>()
  const reconnecting = new Set<Listener>()
  const reconnected = new Set<Listener>()
  const closed = new Set<Listener>()

  connection.on('ImportUpdated', (job: ImportJob) => updated.forEach((l) => l(job)))
  connection.onreconnecting(() => reconnecting.forEach((l) => l()))
  connection.onreconnected(() => reconnected.forEach((l) => l()))
  connection.onclose(() => closed.forEach((l) => l()))

  const subscribe = <T extends unknown[]>(set: Set<Listener<T>>, listener: Listener<T>): Unsubscribe => {
    set.add(listener)
    return () => set.delete(listener)
  }

  // A start right after a stop (React StrictMode remounts) must wait for the stop to finish,
  // and overlapping starts share one attempt instead of failing on SignalR's state check.
  let stopping: Promise<void> = Promise.resolve()
  let starting: Promise<void> | null = null

  return {
    start() {
      if (connection.state === HubConnectionState.Connected) return Promise.resolve()
      starting ??= stopping.then(() => connection.start()).finally(() => {
        starting = null
      })
      return starting
    },
    stop() {
      stopping = connection.stop().catch(() => undefined)
      return stopping
    },
    onImportUpdated: (l) => subscribe(updated, l),
    onReconnecting: (l) => subscribe(reconnecting, l),
    onReconnected: (l) => subscribe(reconnected, l),
    onClose: (l) => subscribe(closed, l),
  }
}
