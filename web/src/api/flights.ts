import { api, unwrap } from './client'

export interface FlightListParams {
  page: number
  pageSize: number
  customerId?: string
  tail?: string
}

export const listFlights = (query: FlightListParams, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights', { params: { query }, signal }))

export const getFlight = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}', { params: { path: { id } }, signal }))

export const getFlightDiagnostics = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/diagnostics', { params: { path: { id } }, signal }))
