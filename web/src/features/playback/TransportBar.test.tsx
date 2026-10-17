import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { TransportStatus } from '../../machines/playbackMachine'
import { TransportBar } from './TransportBar'

function renderBar(status: TransportStatus = 'paused') {
  const props = {
    status,
    speed: 1 as const,
    positionMs: 3_723_000,
    durationMs: 5_400_000,
    startUtc: '2026-03-14T09:30:00Z',
    onToggle: vi.fn(),
    onStep: vi.fn(),
    onSpeed: vi.fn(),
  }
  render(<TransportBar {...props} />)
  return props
}

describe('TransportBar', () => {
  it('shows UTC time and elapsed time at the playhead, and the flight length', () => {
    renderBar()
    expect(screen.getByText('10:32:03Z')).toBeInTheDocument()
    expect(screen.getByText('T+01:02:03')).toBeInTheDocument()
    expect(screen.getByText('01:30:00')).toBeInTheDocument()
  })

  it('labels the main button by what it will do', () => {
    renderBar('paused')
    expect(screen.getByRole('button', { name: 'Play' })).toBeInTheDocument()
  })

  it.each([
    ['playing', 'Pause'],
    ['buffering', 'Pause'],
    ['ended', 'Replay from start'],
  ] as const)('when %s the button says %s', (status, label) => {
    renderBar(status)
    expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
  })

  it('announces buffering', () => {
    renderBar('buffering')
    expect(screen.getByRole('status')).toHaveTextContent('Loading')
  })

  it('sends toggle, step and speed commands', async () => {
    const props = renderBar()

    await userEvent.click(screen.getByRole('button', { name: 'Play' }))
    await userEvent.click(screen.getByRole('button', { name: 'Step forward' }))
    await userEvent.click(screen.getByRole('button', { name: 'Step back' }))
    await userEvent.selectOptions(screen.getByLabelText('Playback speed'), '60')

    expect(props.onToggle).toHaveBeenCalledOnce()
    expect(props.onStep.mock.calls).toEqual([[1], [-1]])
    expect(props.onSpeed).toHaveBeenCalledWith(60)
  })
})
