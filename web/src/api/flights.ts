import { api, ApiError, isProblem, unwrap } from './client'
import type { paths } from './schema'

export type FlightListParams = NonNullable<paths['/api/flights']['get']['parameters']['query']>

export const listFlights = (query: FlightListParams, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights', { params: { query }, signal }))

export const listCustomers = (signal?: AbortSignal) => unwrap(api.GET('/api/flights/customers', { signal }))

export const getFlight = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}', { params: { path: { id } }, signal }))

export const getFlightDiagnostics = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/diagnostics', { params: { path: { id } }, signal }))

export async function deleteFlight(id: string): Promise<void> {
  const { response, error } = await api.DELETE('/api/flights/{id}', { params: { path: { id } } })
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined)
}

export const deleteFlights = (ids: string[]) => unwrap(api.POST('/api/flights/delete', { body: { ids } }))

/** Every flight of one database (never across databases). */
export const deleteAllFlights = (databaseId: string) => unwrap(api.DELETE('/api/flights', { params: { query: { confirm: 'DELETE', databaseId } } }))
