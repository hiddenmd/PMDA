import createClient from 'openapi-fetch'
import type { paths } from './schema'
import type { ProblemDetails } from './types'

/** Typed client for the PMDA API. Paths, parameters and bodies are checked against the OpenAPI document. */
export const api = createClient<paths>({ baseUrl: globalThis.location?.origin ?? '' })

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | undefined

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

/** Returns the data of an openapi-fetch result, or throws an ApiError carrying the problem details. */
export async function unwrap<T>(request: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await request
  if (!response.ok || data === undefined) {
    throw new ApiError(response.status, isProblem(error) ? error : undefined)
  }
  return data
}

export function isProblem(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && ('title' in value || 'detail' in value || 'status' in value)
}
