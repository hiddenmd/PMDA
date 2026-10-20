import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DatabaseList } from '../../api/databases'
import { AuthContext } from '../../auth/authContext'
import { DatabasesPanel } from './DatabasesPanel'

const listDatabases = vi.fn()
const setDefaultDatabase = vi.fn()
const removeDatabase = vi.fn()
vi.mock('../../api/databases', async (original) => ({
  ...(await original<typeof import('../../api/databases')>()),
  listDatabases: (...a: unknown[]) => listDatabases(...a),
  setDefaultDatabase: (...a: unknown[]) => setDefaultDatabase(...a),
  removeDatabase: (...a: unknown[]) => removeDatabase(...a),
}))

const settings = { server: 'localhost', database: 'PMDA', authentication: 'Windows' as const, user: null, hasPassword: false, encrypt: true, trustServerCertificate: true }
const list: DatabaseList = {
  defaultImportId: 'main',
  registryError: null,
  databases: [
    { id: 'main', name: 'Main', settings, readOnly: false, enabled: true, isImportDefault: true, state: 'Ready', message: null, flightCount: 1234 },
    { id: 'bravo', name: 'Bravo', settings: { ...settings, database: 'PMDA_B' }, readOnly: false, enabled: true, isImportDefault: false, state: 'Ready', message: null, flightCount: 5 },
    {
      id: 'archive',
      name: 'Archive',
      settings: { ...settings, server: 'sql02', database: 'PMDA_Old' },
      readOnly: true,
      enabled: true,
      isImportDefault: false,
      state: 'Unreachable',
      message: 'A network-related error occurred',
      flightCount: null,
    },
  ],
}

function renderPanel(isAdmin = true) {
  const auth = { user: { id: 'u', userName: 'admin', displayName: 'Admin', role: isAdmin ? ('Admin' as const) : ('Analyst' as const) }, isAdmin, signOut: vi.fn() }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}>
      <AuthContext.Provider value={auth}>
        <DatabasesPanel />
      </AuthContext.Provider>
    </QueryClientProvider>,
  )
}

const row = (name: string) => screen.getByText(name).closest('tr')!

describe('DatabasesPanel', () => {
  beforeEach(() => vi.clearAllMocks())

  // jsdom has no showModal: dialog content is outside the accessibility tree, hence `hidden: true`.
  it('lists every database with its role, state and flight count', async () => {
    listDatabases.mockResolvedValue(list)
    renderPanel()
    expect(await screen.findByText('Main')).toBeInTheDocument()
    expect(within(row('Main')).getByText('Import default')).toBeInTheDocument()
    expect(within(row('Main')).getByText('1,234')).toBeInTheDocument()
    expect(within(row('Archive')).getByText('Read-only')).toBeInTheDocument()
    expect(within(row('Archive')).getByText('Unreachable')).toBeInTheDocument()
    expect(within(row('Archive')).getByText(/network-related error/)).toBeInTheDocument()
    // Only writable, enabled databases other than the default can become the default.
    expect(within(row('Bravo')).getByRole('button', { name: 'Make default' })).toBeInTheDocument()
    expect(within(row('Archive')).queryByRole('button', { name: 'Make default' })).not.toBeInTheDocument()
  })

  it('makes a database the default and removes one after confirming', async () => {
    listDatabases.mockResolvedValue(list)
    setDefaultDatabase.mockResolvedValue({ ...list, defaultImportId: 'bravo' })
    removeDatabase.mockResolvedValue({ ...list, databases: list.databases.slice(0, 2) })
    renderPanel()
    await userEvent.click(within(await screen.findByText('Bravo').then((e) => e.closest('tr')!)).getByRole('button', { name: 'Make default' }))
    expect(setDefaultDatabase).toHaveBeenCalledWith('bravo')

    await userEvent.click(screen.getByRole('button', { name: 'Remove Archive' }))
    expect(screen.getByText(/left untouched/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /remove from the list/i, hidden: true }))
    expect(removeDatabase).toHaveBeenCalledWith('archive')
  })

  it('opens the form to add a database', async () => {
    listDatabases.mockResolvedValue(list)
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: /add database/i }))
    expect(screen.getByLabelText('Name')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /test connection/i, hidden: true })).toBeInTheDocument()
  })

  it('explains itself to analysts', () => {
    renderPanel(false)
    expect(screen.getByText('Only administrators manage databases.')).toBeInTheDocument()
    expect(listDatabases).not.toHaveBeenCalled()
  })
})
