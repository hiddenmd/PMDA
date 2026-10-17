import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createActor } from 'xstate'
import type { ImportHub } from '../api/importHub'
import { backoffDelay, connectionMachine, connectionStatus } from './connectionMachine'

function fakeHub() {
  const listeners = { reconnecting: new Set<() => void>(), reconnected: new Set<() => void>(), close: new Set<() => void>() }
  const sub = (set: Set<() => void>) => (l: () => void) => {
    set.add(l)
    return () => set.delete(l)
  }
  let failNextStart = false
  const hub = {
    start: vi.fn(async () => {
      if (failNextStart) {
        failNextStart = false
        throw new Error('refused')
      }
    }),
    stop: vi.fn(async () => undefined),
    onImportUpdated: () => () => undefined,
    onReconnecting: sub(listeners.reconnecting),
    onReconnected: sub(listeners.reconnected),
    onClose: sub(listeners.close),
  } satisfies ImportHub
  return {
    hub,
    failNextStart: () => {
      failNextStart = true
    },
    emit: (name: keyof typeof listeners) => listeners[name].forEach((l) => l()),
    listenerCount: () => listeners.close.size,
  }
}

describe('connectionMachine', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  const start = (fake: ReturnType<typeof fakeHub>) => {
    const actor = createActor(connectionMachine, { input: { hub: fake.hub } }).start()
    return { actor, status: () => connectionStatus(actor.getSnapshot()) }
  }

  it('connects on CONNECT', async () => {
    const fake = fakeHub()
    const { actor, status } = start(fake)
    expect(status()).toBe('offline')

    actor.send({ type: 'CONNECT' })
    expect(status()).toBe('connecting')
    await vi.advanceTimersByTimeAsync(0)

    expect(status()).toBe('connected')
    expect(fake.hub.start).toHaveBeenCalledOnce()
  })

  it('retries a failed start after the backoff', async () => {
    const fake = fakeHub()
    fake.failNextStart()
    const { actor, status } = start(fake)

    actor.send({ type: 'CONNECT' })
    await vi.advanceTimersByTimeAsync(0)
    expect(actor.getSnapshot().value).toBe('waiting')
    expect(status()).toBe('offline')

    await vi.advanceTimersByTimeAsync(backoffDelay(1) - 1)
    expect(fake.hub.start).toHaveBeenCalledOnce()

    await vi.advanceTimersByTimeAsync(1)
    expect(fake.hub.start).toHaveBeenCalledTimes(2)
    expect(status()).toBe('connected')
    expect(actor.getSnapshot().context.attempt).toBe(0)
  })

  it('backoff doubles and caps at 30 seconds', () => {
    expect([1, 2, 3, 4, 5, 6, 7, 20].map(backoffDelay)).toEqual([1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000])
  })

  it('follows SignalR automatic reconnects', async () => {
    const fake = fakeHub()
    const { actor, status } = start(fake)
    actor.send({ type: 'CONNECT' })
    await vi.advanceTimersByTimeAsync(0)

    fake.emit('reconnecting')
    expect(status()).toBe('reconnecting')

    fake.emit('reconnected')
    expect(status()).toBe('connected')
  })

  it('restarts with backoff after the connection closes', async () => {
    const fake = fakeHub()
    const { actor, status } = start(fake)
    actor.send({ type: 'CONNECT' })
    await vi.advanceTimersByTimeAsync(0)

    fake.emit('close')
    expect(status()).toBe('offline')

    await vi.advanceTimersByTimeAsync(backoffDelay(1))
    expect(status()).toBe('connected')
    expect(fake.hub.start).toHaveBeenCalledTimes(2)
  })

  it('DISCONNECT stops the hub and does not reconnect on the resulting close', async () => {
    const fake = fakeHub()
    const { actor, status } = start(fake)
    actor.send({ type: 'CONNECT' })
    await vi.advanceTimersByTimeAsync(0)

    actor.send({ type: 'DISCONNECT' })
    fake.emit('close')
    await vi.advanceTimersByTimeAsync(60_000)

    expect(fake.hub.stop).toHaveBeenCalledOnce()
    expect(status()).toBe('offline')
    expect(fake.hub.start).toHaveBeenCalledOnce()
  })

  it('unsubscribes from the hub when stopped', () => {
    const fake = fakeHub()
    const { actor } = start(fake)
    expect(fake.listenerCount()).toBe(1)

    actor.stop()

    expect(fake.listenerCount()).toBe(0)
  })
})
