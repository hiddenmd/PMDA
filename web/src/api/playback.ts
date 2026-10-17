import { api, unwrap } from './client'

export interface FramesQuery {
  fromMs: number
  toMs: number
  stepMs: number
}

export const getFrames = (flightId: string, query: FramesQuery, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/frames', { params: { path: { id: flightId }, query }, signal }))

export const getOverview = (flightId: string, buckets: number, signal?: AbortSignal) =>
  unwrap(api.GET('/api/flights/{id}/overview', { params: { path: { id: flightId }, query: { buckets } }, signal }))
