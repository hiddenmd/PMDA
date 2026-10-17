import { api, unwrap } from './client'
import type { paths } from './schema'

type Query<P extends keyof paths> = NonNullable<paths[P]['get'] extends { parameters: { query?: infer Q } } ? Q : never>

export type EventsQuery = Query<'/api/flights/{id}/events'>
export type ParameterQuery = Query<'/api/flights/{id}/series/parameter'>
export type ScatterQuery = Query<'/api/flights/{id}/series/scatter'>
export type WindowQuery = Query<'/api/flights/{id}/series/profile'>

export const getEvents = (id: string, query: EventsQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/events', { params: { path: { id }, query }, signal }))

export const getParameterSeries = (id: string, query: ParameterQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/series/parameter', { params: { path: { id }, query }, signal }))

export const getScatterSeries = (id: string, query: ScatterQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/series/scatter', { params: { path: { id }, query }, signal }))

export const getProfileSeries = (id: string, query: WindowQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/series/profile', { params: { path: { id }, query }, signal }))

export const getRateSeries = (id: string, query: WindowQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/series/rate', { params: { path: { id }, query }, signal }))
