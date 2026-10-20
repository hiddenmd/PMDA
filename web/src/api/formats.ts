import { api, ApiError, isProblem, unwrap } from './client'
import type { components } from './schema'

type Schemas = components['schemas']
export type FormatSummary = Schemas['FormatSummaryDto']
export type FormatDetail = Schemas['FormatDetailDto']
export type FormatMessage = Schemas['FormatMessageDto']
export type FormatColumn = Schemas['FormatColumnDto']
export type FormatDiagnostic = Schemas['FormatDiagnosticDto']
export type FormatProject = Schemas['FormatProject']
export type FormatUploadResult = Schemas['FormatUploadResultDto']
export type FormatDiff = NonNullable<Schemas['FormatDiff']>
export type DryRunReport = Schemas['DryRunDto']

export const formatKeys = {
  all: ['formats'] as const,
  list: ['formats', 'list'] as const,
  detail: (id: string) => ['formats', 'detail', id] as const,
}

/** Header-defined recording formats (any signed-in user: they are the import choices). */
export const listFormats = (signal?: AbortSignal) => unwrap(api.GET('/api/formats', { signal }))

export const getFormat = (id: string, signal?: AbortSignal) => unwrap(api.GET('/api/formats/{id}', { params: { path: { id } }, signal }))

export const createFormat = (id: string, name: string) => unwrap(api.POST('/api/formats', { body: { id, name } }))

export const addDemoFormat = () => unwrap(api.POST('/api/formats/demo'))

export const saveFormat = (project: FormatProject) =>
  unwrap(api.PUT('/api/formats/{id}', { params: { path: { id: project.id } }, body: project }))

export async function deleteFormat(id: string): Promise<void> {
  const { response, error } = await api.DELETE('/api/formats/{id}', { params: { path: { id } } })
  if (!response.ok) throw new ApiError(response.status, isProblem(error) ? error : undefined)
}

/** Uploads a vendor .h file as the project's next version. */
export const uploadHeader = (id: string, file: File) => postFile<FormatUploadResult>(`/api/formats/${encodeURIComponent(id)}/header`, file, 'PUT')

/** Decodes a recording with the project's current header and recipe; nothing is stored. */
export const dryRun = (id: string, file: File) => postFile<DryRunReport>(`/api/formats/${encodeURIComponent(id)}/dry-run`, file, 'POST')

async function postFile<T>(path: string, file: File, method: 'POST' | 'PUT'): Promise<T> {
  const form = new FormData()
  form.append('file', file, file.name)
  const response = await fetch(path, { method, body: form, headers: { Accept: 'application/json' }, credentials: 'same-origin' })
  const body: unknown = await response.json().catch(() => undefined)
  if (!response.ok) throw new ApiError(response.status, isProblem(body) ? body : undefined)
  return body as T
}
