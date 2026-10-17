import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { FlightSummary } from '../../api/types'
import { FlightsTable } from './FlightsTable'

const flight = (overrides: Partial<FlightSummary>): FlightSummary => ({
  id: 'f-1',
  customerId: 'SAMPLE',
  tail: 'TAIL-042',
  missionId: 'MSN-1',
  startUtc: '2026-03-14T09:30:00Z',
  status: 'Completed',
  eventCount: 1500,
  errorCount: 0,
  importedUtc: '2026-10-17T08:00:00Z',
  ...overrides,
})

describe('FlightsTable', () => {
  it('shows an empty message without flights', () => {
    render(<FlightsTable flights={[]} selectedId={null} onSelect={vi.fn()} />)
    expect(screen.getByText(/No flights yet/)).toBeInTheDocument()
  })

  it('renders a row per flight and marks the selected one', () => {
    render(
      <FlightsTable
        flights={[flight({ id: 'a', tail: 'AAA' }), flight({ id: 'b', tail: 'BBB', status: 'Failed' })]}
        selectedId="b"
        onSelect={vi.fn()}
      />,
    )

    const rows = screen.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('1,500')).toBeInTheDocument()
    expect(rows[1]).toHaveAttribute('aria-selected', 'true')
    expect(within(rows[1]).getByText('Failed')).toBeInTheDocument()
  })

  it('selects a flight once per click, by mouse or keyboard', async () => {
    const onSelect = vi.fn()
    render(<FlightsTable flights={[flight({ id: 'a', tail: 'AAA' })]} selectedId={null} onSelect={onSelect} />)

    await userEvent.click(screen.getByRole('button', { name: 'AAA' }))
    expect(onSelect).toHaveBeenCalledTimes(1)

    screen.getByRole('button', { name: 'AAA' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(onSelect).toHaveBeenCalledTimes(2)
    expect(onSelect).toHaveBeenLastCalledWith('a')
  })
})
