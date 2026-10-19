import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, describe, expect, it } from 'vitest'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { deriveAttitude } from './attitude'
import { AttitudeView, type AttitudeReadout } from './AttitudeView'

beforeAll(() => {
  // jsdom has no canvas: return no context quietly instead of logging "not implemented".
  HTMLCanvasElement.prototype.getContext = (() => null) as unknown as HTMLCanvasElement['getContext']
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
})
