import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DatabaseForm } from './DatabaseForm'

const testDatabase = vi.fn()
const saveDatabase = vi.fn()
vi.mock('../../api/database', async (original) => ({
  ...(await original<typeof import('../../api/database')>()),
  testDatabase: (...a: unknown[]) => testDatabase(...a),
  saveDatabase: (...a: unknown[]) => saveDatabase(...a),
}))

const view = (onSaved = vi.fn()) => {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <DatabaseForm
        initial={{ server: 'localhost', database: 'PMDA', authentication: 'Windows', user: null, hasPassword: false, encrypt: true, trustServerCertificate: true }}
        onSaved={onSaved}
      />
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
    await userEvent.click(screen.getByRole('button', { name: /test connection/i }))
    expect(await screen.findByText(/connected · sql server 17\.00\.1000/i)).toBeInTheDocument()
    expect(testDatabase).toHaveBeenCalledWith(expect.objectContaining({ server: 'localhost', database: 'PMDA', authentication: 'Windows' }))
  })

  it('saves and reports the new status', async () => {
    const status = { state: 'Ready', canConfigure: true, source: 'File', settings: null, message: null }
    saveDatabase.mockResolvedValueOnce(status)
    const onSaved = view()
    await userEvent.click(screen.getByLabelText(/create the database/i))
    await userEvent.click(screen.getByRole('button', { name: /save and connect/i }))
    expect(onSaved).toHaveBeenCalledWith(status)
    expect(saveDatabase).toHaveBeenCalledWith(expect.objectContaining({ createIfMissing: true }))
  })
})
