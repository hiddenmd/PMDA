import { api, unwrap } from './client'
import type { components } from './schema'

type Schemas = components['schemas']
export type DatabaseStatus = Schemas['DatabaseStatusDto']
export type DatabaseSettings = Schemas['DatabaseSettingsDto']
export type DatabaseSetupRequest = Schemas['DatabaseSetupRequest']
export type DatabaseTestResult = Schemas['DatabaseTestResultDto']

export const databaseKeys = { all: ['database'] as const, status: ['database', 'status'] as const }

export const getDatabaseStatus = (signal?: AbortSignal) => unwrap(api.GET('/api/database', { signal }))
export const testDatabase = (body: DatabaseSetupRequest) => unwrap(api.POST('/api/database/test', { body }))
export const saveDatabase = (body: DatabaseSetupRequest) => unwrap(api.PUT('/api/database', { body }))
