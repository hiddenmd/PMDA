import { api, unwrap } from './client'

export const listSymbology = (signal?: AbortSignal) => unwrap(api.GET('/api/symbology', { signal }))

export const getSymbology = (id: string, signal?: AbortSignal) => unwrap(api.GET('/api/symbology/{id}', { params: { path: { id } }, signal }))

export const getCustomerSymbology = (customerId: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/symbology/customer/{customerId}', { params: { path: { customerId } }, signal }))
