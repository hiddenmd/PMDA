import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { deriveAttitude } from './attitude'
import { drawAircraft } from './aircraft3d'
import { createChaseScene } from './scene3d/ChaseScene'
import { AttitudeView, type AttitudeReadout } from './AttitudeView'

vi.mock('./scene3d/ChaseScene', () => ({ createChaseScene: vi.fn() }))
vi.mock('./aircraft3d', () => ({ drawAircraft: vi.fn() }))

const create = vi.mocked(createChaseScene)
const fakeScene = () => ({
  setFrame: vi.fn(),
  setPreset: vi.fn(),
  orbitBy: vi.fn(),
  resetView: vi.fn(),
  setColors: vi.fn(),
  resize: vi.fn(),
  render: vi.fn(() => false),
  dispose: vi.fn(),
})
type FakeScene = ReturnType<typeof fakeScene>
const asScene = (s: FakeScene) => s as unknown as Awaited<ReturnType<typeof createChaseScene>>
const flush = () => act(async () => { await new Promise((r) => setTimeout(r, 0)) })

const ctx2d = new Proxy({}, { get: (_t, k) => (k === 'canvas' ? {} : () => ({ addColorStop() {} })), set: () => true })
beforeEach(() => {
  create.mockReset()
  create.mockResolvedValue(null)
})
afterEach(() => vi.unstubAllGlobals())

beforeAll(() => {
  // jsdom has no canvas: return no context quietly instead of logging "not implemented".
  HTMLCanvasElement.prototype.getContext = (() => ctx2d) as unknown as HTMLCanvasElement['getContext']
  globalThis.ResizeObserver ??= class {
    observe() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
  globalThis.IntersectionObserver ??= class {
    observe() {}
    disconnect() {}
  } as unknown as typeof IntersectionObserver
})

const track = { t: [0, 1000, 2000], lat: [0, 0.001, 0.002], lon: [0, 0, 0], altM: [100, 110, 120] }
const actor = { getSnapshot: () => ({ context: { positionMs: 500 } }) } as unknown as PlaybackActor

function view() {
  const readout = { current: {} as AttitudeReadout }
  return render(<AttitudeView actor={actor} series={deriveAttitude(track)} track={track} platform="FixedWing" readout={readout} />)
}

describe('AttitudeView', () => {
  it('offers Chase, Side and Top views, Chase selected', () => {
    view()
    const chase = screen.getByRole('button', { name: 'Chase' })
    expect(chase).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Side' })).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getByRole('button', { name: 'Top' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('switches the selected view', async () => {
    view()
    await userEvent.click(screen.getByRole('button', { name: 'Top' }))
    expect(screen.getByRole('button', { name: 'Top' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('labels the instrument and the aircraft view for assistive technology', () => {
    view()
    expect(screen.getByRole('img', { name: /attitude indicator/i })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /aircraft seen from behind/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reset view' })).toBeInTheDocument()
  })

  it('applies a preset clicked before the scene resolves', async () => {
    let resolve!: (s: ReturnType<typeof asScene>) => void
    create.mockReturnValue(new Promise((r) => (resolve = r)))
    view()
    await userEvent.click(screen.getByRole('button', { name: 'Top' }))
    const scene = fakeScene()
    resolve(asScene(scene))
    await flush()
    expect(scene.setPreset).toHaveBeenLastCalledWith('top')
  })

  it('disposes a scene that resolves after unmount and creates no further scene', async () => {
    let resolve!: (s: ReturnType<typeof asScene>) => void
    create.mockReturnValue(new Promise((r) => (resolve = r)))
    const { unmount } = view()
    await flush()
    unmount()
    const scene = fakeScene()
    resolve(asScene(scene))
    await flush()
    expect(scene.dispose).toHaveBeenCalledTimes(1)
    expect(create).toHaveBeenCalledTimes(1)
  })

  it('draws the 2D fallback when the scene is unavailable', async () => {
    const callbacks: FrameRequestCallback[] = []
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => callbacks.push(cb))
    vi.stubGlobal('cancelAnimationFrame', () => {})
    view()
    await flush()
    for (let i = 0; i < 3 && callbacks.length; i++) callbacks.splice(0).forEach((cb) => cb(1000 + i * 16))
    expect(drawAircraft).toHaveBeenCalled()
  })

  it('gives every mount its own canvas', async () => {
    const first = view()
    await flush()
    first.unmount()
    view()
    await flush()
    const [a, b] = create.mock.calls.map((c) => c[0])
    expect(create).toHaveBeenCalledTimes(2)
    expect(a).not.toBe(b)
  })
})
