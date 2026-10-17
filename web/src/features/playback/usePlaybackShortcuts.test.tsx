import { fireEvent, render, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { PlaybackEvent } from '../../machines/playbackMachine'
import { usePlaybackShortcuts } from './usePlayback'
import type { Speed } from './playbackView'

function setup(speed: Speed = 10) {
  const send = vi.fn<(e: PlaybackEvent) => void>()
  const currentSpeed = () => speed
  renderHook(() => usePlaybackShortcuts(send, currentSpeed))
  return send
}

describe('usePlaybackShortcuts', () => {
  it('Space toggles, arrows step, brackets change speed', () => {
    const send = setup(10)

    fireEvent.keyDown(document.body, { key: ' ' })
    fireEvent.keyDown(document.body, { key: 'ArrowLeft' })
    fireEvent.keyDown(document.body, { key: 'ArrowRight' })
    fireEvent.keyDown(document.body, { key: ']' })
    fireEvent.keyDown(document.body, { key: '[' })

    expect(send.mock.calls.map(([e]) => e)).toEqual([
      { type: 'TOGGLE' },
      { type: 'STEP', direction: -1 },
      { type: 'STEP', direction: 1 },
      { type: 'SET_SPEED', speed: 60 },
      { type: 'SET_SPEED', speed: 1 },
    ])
  })

  it('accepts AltGr and the physical bracket keys of other layouts', () => {
    const send = setup(1)

    fireEvent.keyDown(document.body, { key: ']', ctrlKey: true, altKey: true }) // AltGr
    fireEvent.keyDown(document.body, { key: 'ă', code: 'BracketRight' }) // Romanian layout

    expect(send.mock.calls.map(([e]) => e)).toEqual([
      { type: 'SET_SPEED', speed: 10 },
      { type: 'SET_SPEED', speed: 10 },
    ])
  })

  it('keeps the speed within the available range', () => {
    const fastest = setup(600)
    fireEvent.keyDown(document.body, { key: ']' })
    expect(fastest).toHaveBeenLastCalledWith({ type: 'SET_SPEED', speed: 600 })
  })

  it('ignores keys while typing, Ctrl shortcuts, and keys a control handles itself', () => {
    const send = setup()
    const { getByRole } = render(
      <>
        <input type="text" aria-label="text" />
        <input type="range" aria-label="range" />
        <button type="button">btn</button>
      </>,
    )

    fireEvent.keyDown(getByRole('textbox'), { key: ' ' })
    fireEvent.keyDown(document.body, { key: ']', ctrlKey: true })
    fireEvent.keyDown(getByRole('slider'), { key: 'ArrowRight' })
    fireEvent.keyDown(getByRole('button'), { key: ' ' })
    expect(send).not.toHaveBeenCalled()

    fireEvent.keyDown(getByRole('slider'), { key: ' ' }) // Space on the scrubber still plays
    expect(send).toHaveBeenCalledWith({ type: 'TOGGLE' })
  })
})
