import { api, ApiError, isProblem, unwrap } from './client'
import type { components } from './schema'

type Schemas = components['schemas']
export type CurrentUser = Schemas['CurrentUserDto']
export type UserRole = Schemas['UserRole']
export type AppUser = Schemas['UserDto']
export type AuthState = Schemas['AuthStateDto']

export const getAuthState = (signal?: AbortSignal) => unwrap(api.GET('/api/auth/state', { signal }))

/** The signed-in user, or null when there is no valid session. */
export async function getCurrentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  const { data, response } = await api.GET('/api/auth/me', { signal })
  if (response.status === 401) return null
  if (!response.ok || !data) throw new ApiError(response.status)
  return data
}

export const login = (userName: string, password: string) => unwrap(api.POST('/api/auth/login', { body: { userName, password } }))

export const setupAdministrator = (userName: string, displayName: string, password: string) =>
  unwrap(api.POST('/api/auth/setup', { body: { userName, displayName, password } }))

export async function logout(): Promise<void> {
  await api.POST('/api/auth/logout')
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const { response, error } = await api.POST('/api/auth/password', { body: { currentPassword, newPassword } })
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined)
}

export const listUsers = (signal?: AbortSignal) => unwrap(api.GET('/api/users', { signal }))

export const createUser = (body: { userName: string; displayName: string; password: string; role: UserRole }) =>
  unwrap(api.POST('/api/users', { body }))

export const updateUser = (id: string, body: { displayName: string; role: UserRole; isActive: boolean }) =>
  unwrap(api.PUT('/api/users/{id}', { params: { path: { id } }, body }))

export async function resetUserPassword(id: string, newPassword: string): Promise<void> {
  const { response, error } = await api.POST('/api/users/{id}/password', { params: { path: { id } }, body: { newPassword } })
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined)
}

/** The most useful message of an API error: a field error from a validation problem, else the detail. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const errors = (error.problem as { errors?: Record<string, string[]> } | undefined)?.errors
    const first = errors && Object.values(errors)[0]?.[0]
    return first ?? error.message
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}
