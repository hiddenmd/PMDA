import { api, ApiError, isProblem, unwrap } from './client'
import type { paths } from './schema'
import type { ImportJob } from './types'

const uploadPath = '/api/imports' satisfies keyof paths

export interface UploadOptions {
  onProgress?: (loaded: number, total: number) => void
  signal?: AbortSignal
  /** The flight database to import into; the server's import default when omitted. */
  databaseId?: string | null
  /** The recording format (a header-defined project id); the server detects it when omitted. */
  format?: string | null
}

/**
 * Uploads a flight file. Uses XMLHttpRequest because fetch cannot report upload progress.
 * Rejects with an AbortError when the signal aborts, or an ApiError for an error response.
 */
export function uploadImport(file: File, { onProgress, signal, databaseId, format }: UploadOptions = {}): Promise<ImportJob> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Upload aborted', 'AbortError'))
      return
    }

    const xhr = new XMLHttpRequest()
    const onAbort = () => xhr.abort()
    signal?.addEventListener('abort', onAbort, { once: true })
    const done = () => signal?.removeEventListener('abort', onAbort)

    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress?.(e.loaded, e.total)
    }
    xhr.onload = () => {
      done()
      const body = parseJson(xhr.responseText)
      if (xhr.status === 202) resolve(body as ImportJob)
      else reject(new ApiError(xhr.status, isProblem(body) ? body : undefined))
    }
    xhr.onerror = () => {
      done()
      reject(new ApiError(0, { title: 'Network error', detail: 'The upload could not reach the server.' }))
    }
    xhr.onabort = () => {
      done()
      reject(new DOMException('Upload aborted', 'AbortError'))
    }

    const form = new FormData()
    form.append('file', file, file.name)
    if (databaseId) form.append('databaseId', databaseId)
    if (format) form.append('format', format)
    xhr.open('POST', uploadPath)
    xhr.setRequestHeader('Accept', 'application/json')
    xhr.send(form)
  })
}

export const getImport = (id: string, signal?: AbortSignal) =>
  unwrap(api.GET('/api/imports/{id}', { params: { path: { id } }, signal }))

/** Requests cancellation. A 409 (already finished) resolves to null: the job update tells the outcome. */
export async function cancelImport(id: string): Promise<ImportJob | null> {
  const { data, response } = await api.DELETE('/api/imports/{id}', { params: { path: { id } } })
  if (response.status === 409) return null
  if (!response.ok || !data) throw new ApiError(response.status)
  return data
}

function parseJson(text: string): unknown {
  try {
    return text ? JSON.parse(text) : undefined
  } catch {
    return undefined
  }
}
