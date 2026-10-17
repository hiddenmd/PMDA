import { describe, expect, it } from 'vitest'
import { createActor, fromCallback, fromPromise } from 'xstate'
import type { PlaybackFrames } from '../api/types'
import { CHUNK_STEPS, PREFETCH_STEPS, playbackMachine, transportStatus, type PlaybackEvent } from './playbackMachine'

interface FakeRequest {
  fromMs: number
  toMs: number
  stepMs: number
  mode: 'replace' | 'append'
  aborted: boolean
  resolve: (frames: PlaybackFrames) => void
  reject: (error: Error) => void
}

const STEP_1X = 33

function harness(durationMs = 600_000) {
  const requests: FakeRequest[] = []
  let tick: ((e: PlaybackEvent) => void) | null = null

  const machine = playbackMachine.provide({
    actors: {
      clock: fromCallback<PlaybackEvent>(({ sendBack }) => {
        tick = sendBack
        return () => {
          tick = null
        }
      }),
      fetchFrames: fromPromise(({ input, signal }) => {
        return new Promise<PlaybackFrames>((resolve, reject) => {
          const request: FakeRequest = { ...input.request, aborted: false, resolve, reject }
          signal.addEventListener('abort', () => (request.aborted = true))
          requests.push(request)
        })
      }),
    },
  })

  const actor = createActor(machine, { input: { flightId: 'f-1', durationMs } }).start()
  const flush = () => new Promise((r) => setTimeout(r, 0))

  /** Resolve the newest request with one frame per step. */
  const respond = async (request = requests.at(-1)!) => {
    const frames = []
    for (let t = request.fromMs; t < request.toMs; t += request.stepMs) frames.push({ offsetMs: t, emitters: [], countermeasures: [], position: null })
    request.resolve({ fromMs: request.fromMs, toMs: request.toMs, stepMs: request.stepMs, frames })
    await flush()
  }

  return {
    actor,
    requests,
    respond,
    flush,
    status: () => transportStatus(actor.getSnapshot()),
    position: () => actor.getSnapshot().context.positionMs,
    tick: (deltaMs: number) => {
      if (!tick) throw new Error('clock is not running')
      tick({ type: 'TICK', deltaMs })
    },
    clockRunning: () => tick !== null,
  }
}

describe('playbackMachine', () => {
  it('loads the first chunk at once and starts paused', () => {
    const h = harness()

    expect(h.status()).toBe('paused')
    expect(h.requests).toHaveLength(1)
    expect(h.requests[0]).toMatchObject({ fromMs: 0, toMs: CHUNK_STEPS * STEP_1X, stepMs: STEP_1X, mode: 'replace' })
  })

  it('buffers until frames arrive, then plays and advances with real time × speed', async () => {
    const h = harness()
    h.actor.send({ type: 'PLAY' })
    expect(h.status()).toBe('buffering')
    expect(h.clockRunning()).toBe(false)

    await h.respond()
    expect(h.status()).toBe('playing')

    h.tick(100)
    expect(h.position()).toBe(100)

    h.actor.send({ type: 'SET_SPEED', speed: 10 })
    expect(h.status()).toBe('buffering') // new step size needs new frames
    await h.respond()
    h.tick(100)
    expect(h.position()).toBe(1100)
  })

  it('prefetches the next chunk before the playhead reaches the end of the loaded frames', async () => {
    const h = harness()
    h.actor.send({ type: 'PLAY' })
    await h.respond()

    const loadedTo = CHUNK_STEPS * STEP_1X
    h.tick(loadedTo - PREFETCH_STEPS * STEP_1X + 1)

    expect(h.requests).toHaveLength(2)
    expect(h.requests[1]).toMatchObject({ fromMs: loadedTo, mode: 'append' })
    expect(h.status()).toBe('playing')

    await h.respond()
    expect(h.actor.getSnapshot().context.loaded?.toMs).toBe(loadedTo + CHUNK_STEPS * STEP_1X)
  })

  it('a seek outside the loaded frames drops the in-flight request and loads around the new position', async () => {
    const h = harness()
    h.actor.send({ type: 'SEEK', positionMs: 300_000 })

    expect(h.requests[0].aborted).toBe(true)
    const reload = h.requests[1]
    expect(reload.mode).toBe('replace')
    expect(reload.fromMs).toBeLessThanOrEqual(300_000 - 3_000) // includes the persistence look-back
    expect(reload.toMs).toBeGreaterThan(300_000)
  })

  it('a seek inside the loaded frames needs no request', async () => {
    const h = harness()
    await h.respond()

    h.actor.send({ type: 'SEEK', positionMs: 1_000 })

    expect(h.requests).toHaveLength(1)
    expect(h.position()).toBe(1_000)
  })

  it('stops at the end and plays again from the start', async () => {
    const h = harness(1_000)
    h.actor.send({ type: 'PLAY' })
    await h.respond()

    h.tick(250)
    h.tick(250)
    h.tick(250)
    h.tick(400)
    expect(h.position()).toBe(1_000) // clamped to the end
    expect(h.status()).toBe('ended')
    expect(h.clockRunning()).toBe(false)

    h.actor.send({ type: 'TOGGLE' })
    expect(h.position()).toBe(0)
    expect(h.status()).toBe('playing')
  })

  it('pause stops the clock; play at the end restarts; seeking back leaves ended', async () => {
    const h = harness(1_000)
    await h.respond()
    h.actor.send({ type: 'PLAY' })
    h.actor.send({ type: 'PAUSE' })
    expect(h.clockRunning()).toBe(false)

    h.actor.send({ type: 'SEEK', positionMs: 5_000 })
    expect(h.position()).toBe(1_000) // clamped
    h.actor.send({ type: 'PLAY' })
    expect(h.position()).toBe(0)
    expect(h.status()).toBe('playing')

    h.tick(250)
    h.tick(250)
    h.tick(250)
    h.tick(250)
    expect(h.status()).toBe('ended')
    h.actor.send({ type: 'SEEK', positionMs: 500 })
    expect(h.status()).toBe('paused')
  })

  it('steps ±10 s at low speed and further at high speed', () => {
    const h = harness()
    h.actor.send({ type: 'STEP', direction: 1 })
    expect(h.position()).toBe(10_000)
    h.actor.send({ type: 'SET_SPEED', speed: 600 })
    h.actor.send({ type: 'STEP', direction: 1 })
    expect(h.position()).toBe(600_000) // +600 s, clamped to the 600 s flight
    h.actor.send({ type: 'STEP', direction: -1 })
    expect(h.position()).toBe(0)
  })

  it('retries after a failed request', async () => {
    const h = harness()
    h.requests[0].reject(new Error('boom'))
    await h.flush()

    expect(h.actor.getSnapshot().context.error).toMatch(/Retrying/)
    expect(h.actor.getSnapshot().matches({ loader: 'failed' })).toBe(true)

    h.actor.send({ type: 'SEEK', positionMs: 0 }) // a seek retries at once
    expect(h.requests).toHaveLength(2)
    expect(h.actor.getSnapshot().context.error).toBeNull()
  })
})
