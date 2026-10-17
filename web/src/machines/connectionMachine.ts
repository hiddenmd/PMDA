import { assign, fromCallback, fromPromise, setup, type SnapshotFrom } from 'xstate'
import type { ImportHub } from '../api/importHub'

/**
 * Lifecycle of the live connection to the API hub:
 *
 *   disconnected ─CONNECT→ connecting ─ok→ connected ⇄ reconnecting
 *                              │ fail          │ closed       │ closed
 *                              └──→ waiting ←──┴──────────────┘
 *                                     └─(backoff)→ connecting
 *
 * SignalR's own automatic reconnect covers short drops (reconnecting); when it gives up the
 * connection closes and this machine retries with exponential backoff.
 */

export type ConnectionEvent =
  | { type: 'CONNECT' }
  | { type: 'DISCONNECT' }
  | { type: 'HUB_RECONNECTING' }
  | { type: 'HUB_RECONNECTED' }
  | { type: 'HUB_CLOSED' }

export interface ConnectionContext {
  hub: ImportHub
  attempt: number
}

export const MAX_BACKOFF_MS = 30_000

/** 1 s, 2 s, 4 s … capped at 30 s. */
export const backoffDelay = (attempt: number) => Math.min(MAX_BACKOFF_MS, 1_000 * 2 ** Math.max(0, attempt - 1))

export const connectionMachine = setup({
  types: {
    context: {} as ConnectionContext,
    events: {} as ConnectionEvent,
    input: {} as { hub: ImportHub },
  },
  actors: {
    startHub: fromPromise<void, { hub: ImportHub }>(({ input }) => input.hub.start()),
    hubEvents: fromCallback<ConnectionEvent, { hub: ImportHub }>(({ input, sendBack }) => {
      const subscriptions = [
        input.hub.onReconnecting(() => sendBack({ type: 'HUB_RECONNECTING' })),
        input.hub.onReconnected(() => sendBack({ type: 'HUB_RECONNECTED' })),
        input.hub.onClose(() => sendBack({ type: 'HUB_CLOSED' })),
      ]
      return () => subscriptions.forEach((unsubscribe) => unsubscribe())
    }),
  },
  actions: {
    stopHub: ({ context }) => void context.hub.stop(),
    resetAttempts: assign({ attempt: 0 }),
    countAttempt: assign({ attempt: ({ context }) => context.attempt + 1 }),
  },
  delays: {
    backoff: ({ context }) => backoffDelay(context.attempt),
  },
}).createMachine({
  id: 'connection',
  context: ({ input }) => ({ hub: input.hub, attempt: 0 }),
  invoke: { src: 'hubEvents', input: ({ context }) => ({ hub: context.hub }) },
  on: {
    DISCONNECT: { target: '.disconnected', actions: ['stopHub', 'resetAttempts'] },
  },
  initial: 'disconnected',
  states: {
    disconnected: {
      on: { CONNECT: 'connecting' },
    },
    connecting: {
      invoke: {
        src: 'startHub',
        input: ({ context }) => ({ hub: context.hub }),
        onDone: { target: 'connected', actions: 'resetAttempts' },
        onError: { target: 'waiting', actions: 'countAttempt' },
      },
    },
    connected: {
      on: {
        HUB_RECONNECTING: 'reconnecting',
        HUB_CLOSED: { target: 'waiting', actions: 'countAttempt' },
      },
    },
    reconnecting: {
      on: {
        HUB_RECONNECTED: 'connected',
        HUB_CLOSED: { target: 'waiting', actions: 'countAttempt' },
      },
    },
    waiting: {
      after: { backoff: 'connecting' },
      on: { CONNECT: 'connecting' },
    },
  },
})

export type ConnectionSnapshot = SnapshotFrom<typeof connectionMachine>

export type ConnectionStatus = 'connected' | 'connecting' | 'reconnecting' | 'offline'

export function connectionStatus(snapshot: ConnectionSnapshot): ConnectionStatus {
  if (snapshot.matches('connected')) return 'connected'
  if (snapshot.matches('reconnecting')) return 'reconnecting'
  if (snapshot.matches('connecting')) return snapshot.context.attempt === 0 ? 'connecting' : 'reconnecting'
  return 'offline'
}
