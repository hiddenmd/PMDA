import { api, unwrap } from './client'
import type { paths } from './schema'

export type FlightListParams = NonNullable<paths['/api/flights']['get']['parameters']['query']>

export const listFlights = (query: FlightListParams, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights', { params: { query }, signal }))

export const listCustomers = (signal?: AbortSignal) => unwrap(api.GET('/api/flights/customers', { signal }))

export const getFlight = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}', { params: { path: { id } }, signal }))

export const getFlightDiagnostics = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/diagnostics', { params: { path: { id } }, signal }))
