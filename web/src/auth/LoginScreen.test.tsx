import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { authKeys } from './authContext'
import { LoginScreen } from './LoginScreen'

const login = vi.fn()
vi.mock('../api/auth', async (original) => ({ ...(await original<typeof import('../api/auth')>()), login: (...args: unknown[]) => login(...args) }))

function renderLogin() {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <LoginScreen />
    </QueryClientProvider>,
  )
  return client
}

describe('LoginScreen', () => {
  it('signs in and stores the user', async () => {
    login.mockResolvedValueOnce({ id: 'u1', userName: 'ana', displayName: 'Ana', role: 'Analyst' })
    const client = renderLogin()
    const submit = screen.getByRole('button', { name: /sign in/i })
    expect(submit).toBeDisabled()

    await userEvent.type(screen.getByLabelText('User name'), ' ana ')
    await userEvent.type(screen.getByLabelText('Password'), 'secret-pass')
    await userEvent.click(submit)

    expect(login).toHaveBeenCalledWith('ana', 'secret-pass')
    await vi.waitFor(() => expect(client.getQueryData(authKeys.me)).toMatchObject({ userName: 'ana' }))
  })

  it('shows the server message on failure', async () => {
    login.mockRejectedValueOnce(new ApiError(401, { detail: 'The user name or password is not correct.' }))
    renderLogin()

    await userEvent.type(screen.getByLabelText('User name'), 'ana')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('not correct')
  })

  it('can reveal the password', async () => {
    renderLogin()
    const field = screen.getByLabelText('Password')
    expect(field).toHaveAttribute('type', 'password')

    await userEvent.click(screen.getByRole('button', { name: 'Show password' }))

    expect(field).toHaveAttribute('type', 'text')
  })
})
