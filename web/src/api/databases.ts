import { api, ApiError, isProblem, unwrap } from './client'
import type { components } from './schema'

type Schemas = components['schemas']
export type DatabaseList = Schemas['DatabaseListDto']
export type DatabaseInfo = Schemas['DatabaseDto']
export type DatabaseState = Schemas['DatabaseState']
export type DatabaseEntryRequest = Schemas['DatabaseEntryRequest']
export type DatabaseTestResult = Schemas['DatabaseTestResultDto']
export type DatabaseChoices = Schemas['DatabaseChoicesDto']
export type DatabaseChoice = Schemas['DatabaseChoiceDto']

export const databaseKeys = {
  all: ['databases'] as const,
  list: ['databases', 'list'] as const,
  choices: ['databases', 'choices'] as const,
}

/** Every registered database with its state (admins). */
export const listDatabases = (signal?: AbortSignal) => unwrap(api.GET('/api/databases', { signal }))

/** Enabled databases anyone may filter by or import into (unless read-only), and the import default. */
export const getDatabaseChoices = (signal?: AbortSignal) => unwrap(api.GET('/api/databases/choices', { signal }))

export const testDatabase = (body: DatabaseEntryRequest) => unwrap(api.POST('/api/databases/test', { body }))
export const addDatabase = (body: DatabaseEntryRequest) => unwrap(api.POST('/api/databases', { body }))
export const updateDatabase = (id: string, body: DatabaseEntryRequest) =>
  unwrap(api.PUT('/api/databases/{id}', { params: { path: { id } }, body }))
export const setDefaultDatabase = (id: string) => unwrap(api.PUT('/api/databases/default', { body: { id } }))

export async function removeDatabase(id: string): Promise<DatabaseList> {
  const { data, response, error } = await api.DELETE('/api/databases/{id}', { params: { path: { id } } })
  if (!response.ok || !data) throw new ApiError(response.status, isProblem(error) ? error : undefined)
  return data
}
