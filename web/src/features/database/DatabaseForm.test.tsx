import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { DatabaseInfo } from '../../api/databases'
import { DatabaseForm } from './DatabaseForm'

const testDatabase = vi.fn()
const addDatabase = vi.fn()
const updateDatabase = vi.fn()
vi.mock('../../api/databases', async (original) => ({
  ...(await original<typeof import('../../api/databases')>()),
  testDatabase: (...a: unknown[]) => testDatabase(...a),
  addDatabase: (...a: unknown[]) => addDatabase(...a),
  updateDatabase: (...a: unknown[]) => updateDatabase(...a),
}))

const archive: DatabaseInfo = {
  id: 'archive',
  name: 'Archive',
  settings: { server: 'sql02', database: 'PMDA_Archive', authentication: 'SqlLogin', user: 'reader', hasPassword: true, encrypt: true, trustServerCertificate: false },
  readOnly: true,
  enabled: true,
  isImportDefault: false,
  state: 'Ready',
  message: null,
  flightCount: 12,
}

const view = (initial?: DatabaseInfo, onSaved = vi.fn()) => {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <DatabaseForm initial={initial} onSaved={onSaved} />
    </QueryClientProvider>,
  )
  return onSaved
}

describe('DatabaseForm', () => {
  it('shows login fields only for SQL logins', async () => {
    view()
    expect(screen.queryByLabelText('Login')).not.toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('SQL Server login'))
    expect(screen.getByLabelText('Login')).toBeInTheDocument()
    expect(screen.getByLabelText(/^password/i)).toBeInTheDocument()
  })

  it('tests the connection and reports the server', async () => {
    testDatabase.mockResolvedValueOnce({ ok: true, message: null, serverVersion: '17.00.1000', databaseExists: true })
    view()
    await userEvent.type(screen.getByLabelText('Database'), 'PMDA_B')
    await userEvent.click(screen.getByRole('button', { name: /test connection/i }))
    expect(await screen.findByText(/connected · sql server 17\.00\.1000/i)).toBeInTheDocument()
    expect(testDatabase).toHaveBeenCalledWith(expect.objectContaining({ server: 'localhost', database: 'PMDA_B', authentication: 'Windows' }))
  })

  it('adds a new database', async () => {
    const list = { databases: [], defaultImportId: null, registryError: null }
    addDatabase.mockResolvedValueOnce(list)
    const onSaved = view()
    await userEvent.type(screen.getByLabelText('Name'), 'Exercise')
    await userEvent.type(screen.getByLabelText('Database'), 'PMDA_Exercise')
    await userEvent.click(screen.getByLabelText(/create the database/i))
    await userEvent.click(screen.getByRole('button', { name: /add database/i }))
    expect(onSaved).toHaveBeenCalledWith(list)
    expect(addDatabase).toHaveBeenCalledWith(expect.objectContaining({ id: null, name: 'Exercise', createIfMissing: true, readOnly: false }))
  })

  it('edits an existing one, keeping its stored password', async () => {
    updateDatabase.mockResolvedValueOnce({ databases: [], defaultImportId: null, registryError: null })
    view(archive)
    expect(screen.getByText(/stored and encrypted/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/create the database/i)).not.toBeInTheDocument() // read-only: never created
    await userEvent.clear(screen.getByLabelText('Name'))
    await userEvent.type(screen.getByLabelText('Name'), 'Archive 2025')
    await userEvent.click(screen.getByRole('button', { name: /save changes/i }))
    expect(updateDatabase).toHaveBeenCalledWith('archive', expect.objectContaining({ id: 'archive', name: 'Archive 2025', password: null, readOnly: true }))
  })
})
