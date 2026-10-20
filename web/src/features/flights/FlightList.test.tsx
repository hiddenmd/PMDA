import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { FlightSummary } from '../../api/types'
import { FlightItems } from './FlightList'

const flight = (overrides: Partial<FlightSummary>): FlightSummary => ({
  id: 'f-1',
  customerId: 'SAMPLE',
  tail: 'TAIL-042',
  missionId: 'MSN-1',
  startUtc: '2026-03-14T09:30:00Z',
  status: 'Completed',
  eventCount: 1_500_000,
  errorCount: 0,
  importedUtc: '2026-10-17T08:00:00Z',
  databaseId: 'main',
  databaseName: 'Main',
  ...overrides,
})

describe('FlightItems', () => {
  it('shows an empty message without flights', () => {
    render(<FlightItems flights={[]} selectedId={null} onSelect={vi.fn()} />)
    expect(screen.getByText(/No flights yet/)).toBeInTheDocument()
  })

  it('lists flights with status text, compact counts and the current one marked', () => {
    render(
      <FlightItems
        flights={[flight({ id: 'a', tail: 'AAA' }), flight({ id: 'b', tail: 'BBB', status: 'Failed', errorCount: 3 })]}
        selectedId="b"
        onSelect={vi.fn()}
      />,
    )

    const [a, b] = screen.getAllByRole('button')
    expect(a).toHaveTextContent('AAA')
    expect(within(a).getByTitle('1,500,000 events')).toHaveTextContent('1.5M ev')
    expect(a).toHaveAttribute('aria-current', 'false')
    expect(b).toHaveAttribute('aria-current', 'true')
    expect(b).toHaveTextContent('Failed')
    expect(b).toHaveTextContent('3 prob')
  })

  it('names each flight\'s database when there are several', () => {
    render(<FlightItems flights={[flight({ id: 'a', databaseName: 'Main' }), flight({ id: 'b', databaseName: 'Archive 2025' })]} selectedId={null} onSelect={vi.fn()} showDatabase />)
    expect(screen.getByText('Archive 2025')).toBeInTheDocument()
    expect(screen.getByText('Main')).toBeInTheDocument()
  })

  it('selects a flight by mouse or keyboard', async () => {
    const onSelect = vi.fn()
    render(<FlightItems flights={[flight({ id: 'a', tail: 'AAA' })]} selectedId={null} onSelect={onSelect} />)

    await userEvent.click(screen.getByRole('button', { name: /AAA/ }))
    screen.getByRole('button', { name: /AAA/ }).focus()
    await userEvent.keyboard('{Enter}')

    expect(onSelect).toHaveBeenCalledTimes(2)
    expect(onSelect).toHaveBeenLastCalledWith('a')
  })
})
