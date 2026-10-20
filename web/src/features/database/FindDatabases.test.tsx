import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DiscoveryResult } from '../../api/databases'
import { FindDatabases } from './FindDatabases'

const discoverDatabases = vi.fn()
const addDatabase = vi.fn()
vi.mock('../../api/databases', async (original) => ({
  ...(await original<typeof import('../../api/databases')>()),
  discoverDatabases: (...a: unknown[]) => discoverDatabases(...a),
  addDatabase: (...a: unknown[]) => addDatabase(...a),
}))

const found: DiscoveryResult = {
  server: 'localhost',
  serverVersion: '17.00.1000',
  error: null,
  databases: [
    { name: 'PMDA_Ops_Jordan', isPmda: true, flightCount: 3, registeredId: null, registeredName: null, isTest: false, suggestedName: 'Ops Jordan', canAdd: true },
    { name: 'PMDA_Training', isPmda: true, flightCount: 5, registeredId: null, registeredName: null, isTest: false, suggestedName: 'Training', canAdd: true },
    { name: 'PMDA', isPmda: true, flightCount: 2, registeredId: 'main', registeredName: 'Main', isTest: false, suggestedName: 'PMDA', canAdd: true },
    { name: 'Payroll', isPmda: false, flightCount: null, registeredId: null, registeredName: null, isTest: false, suggestedName: 'Payroll', canAdd: true },
  ],
}

const view = (onAdded = vi.fn()) => {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <FindDatabases onAdded={onAdded} />
    </QueryClientProvider>,
  )
  return onAdded
}

describe('FindDatabases', () => {
  beforeEach(() => vi.clearAllMocks())

  it('searches a server and marks what is already listed', async () => {
    discoverDatabases.mockResolvedValue(found)
    view()
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByText(/2 not in the list yet/)).toBeInTheDocument()
    expect(screen.getByText('In the list: Main')).toBeInTheDocument()
    expect(screen.getByText('Will be listed as “Ops Jordan”')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Payroll' })).not.toBeInTheDocument()
    expect(discoverDatabases).toHaveBeenCalledWith(expect.objectContaining({ server: 'localhost', authentication: 'Windows', password: null }))
  })

  it('adds one database, read-only when ticked', async () => {
    discoverDatabases.mockResolvedValue(found)
    const list = { databases: [], defaultImportId: 'main', registryError: null }
    addDatabase.mockResolvedValue(list)
    const onAdded = view()
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    await userEvent.click(await screen.findByLabelText('Add PMDA_Training as read-only'))
    await userEvent.click(screen.getByRole('button', { name: 'Add PMDA_Training' }))
    expect(addDatabase).toHaveBeenCalledWith(expect.objectContaining({ name: 'Training', database: 'PMDA_Training', readOnly: true, createIfMissing: false }))
    expect(onAdded).toHaveBeenCalledWith(list)
  })

  it('adds all of them at once', async () => {
    discoverDatabases.mockResolvedValue(found)
    addDatabase.mockResolvedValue({ databases: [], defaultImportId: 'main', registryError: null })
    view()
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    await userEvent.click(await screen.findByRole('button', { name: /add all 2/i }))
    expect(addDatabase).toHaveBeenCalledTimes(2)
    expect(addDatabase.mock.calls.map(([body]) => body.database)).toEqual(['PMDA_Ops_Jordan', 'PMDA_Training'])
  })
})
