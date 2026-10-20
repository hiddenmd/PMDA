import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AuthGate } from './AuthGate'

const getDatabaseStatus = vi.fn()
const getCurrentUser = vi.fn()
vi.mock('../api/database', async (original) => ({
  ...(await original<typeof import('../api/database')>()),
  getDatabaseStatus: (...a: unknown[]) => getDatabaseStatus(...a),
}))
vi.mock('../api/auth', async (original) => ({
  ...(await original<typeof import('../api/auth')>()),
  getCurrentUser: (...a: unknown[]) => getCurrentUser(...a),
}))

const gate = () =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthGate>
        <p>The app</p>
      </AuthGate>
    </QueryClientProvider>,
  )

describe('AuthGate', () => {
  it('explains where to set up the database when this machine may not', async () => {
    getDatabaseStatus.mockResolvedValueOnce({ state: 'NotConfigured', canConfigure: false, source: 'None', settings: null, message: null })
    gate()
    expect(await screen.findByText(/isn't connected to a database yet/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /test connection/i })).not.toBeInTheDocument()
    expect(getCurrentUser).not.toHaveBeenCalled()
  })

  it('offers the connection form on the server machine', async () => {
    getDatabaseStatus.mockResolvedValueOnce({ state: 'NotConfigured', canConfigure: true, source: 'None', settings: null, message: null })
    gate()
    expect(await screen.findByRole('button', { name: /test connection/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /connect pmda to sql server/i })).toBeInTheDocument()
  })

  it('shows the app once the database works and a session exists', async () => {
    getDatabaseStatus.mockResolvedValueOnce({ state: 'Ready', canConfigure: false, source: 'AppSettings', settings: null, message: null })
    getCurrentUser.mockResolvedValueOnce({ id: 'u1', userName: 'ana', displayName: 'Ana', role: 'Analyst' })
    gate()
    expect(await screen.findByText('The app')).toBeInTheDocument()
  })
})
