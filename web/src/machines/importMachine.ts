import { assign, fromCallback, fromPromise, setup, type SnapshotFrom } from 'xstate'
import { ApiError } from '../api/client'
import { cancelImport, getImport, uploadImport } from '../api/imports'
import type { ImportJob } from '../api/types'

/**
 * One file import as seen by the user:
 *
 *   idle ─START→ uploading ─UPLOAD_DONE→ tracking ─(job final)→ completed | duplicate | failed | cancelled
 *
 * `tracking` runs two regions in parallel:
 *   phase:   queued → processing                (follows the server job state)
 *   channel: live ⇄ polling                     (hub connected → push, otherwise poll)
 * plus a `request` region for an in-flight cancel request.
 *
 * All I/O is in the actors below; tests replace them with `.provide()`.
 */

export interface ImportContext {
  file: File | null
  /** Where the file is imported (null: the server's default). Kept for retries. */
  databaseId: string | null
  job: ImportJob | null
  upload: { loaded: number; total: number }
  error: string | null
  hubConnected: boolean
}

export type ImportEvent =
  | { type: 'START'; file: File; databaseId?: string | null }
  | { type: 'CANCEL' }
  | { type: 'RETRY' }
  | { type: 'RESET' }
  | { type: 'HUB_STATUS'; connected: boolean }
  | { type: 'JOB_UPDATED'; job: ImportJob }
  | { type: 'JOB_LOST' }
  | { type: 'UPLOAD_PROGRESS'; loaded: number; total: number }
  | { type: 'UPLOAD_DONE'; job: ImportJob }
  | { type: 'UPLOAD_FAILED'; error: string }

export interface ImportInput {
  hubConnected?: boolean
}

export const POLL_INTERVAL_MS = 1_000

/** Uploads the file; reports progress and the result as events. Leaving `uploading` aborts it. */
export const uploadActor = fromCallback<ImportEvent, { file: File; databaseId: string | null }>(({ input, sendBack }) => {
  const controller = new AbortController()
  uploadImport(input.file, {
    signal: controller.signal,
    databaseId: input.databaseId,
    onProgress: (loaded, total) => sendBack({ type: 'UPLOAD_PROGRESS', loaded, total }),
  }).then(
    (job) => sendBack({ type: 'UPLOAD_DONE', job }),
    (error: unknown) => {
      if (!controller.signal.aborted) sendBack({ type: 'UPLOAD_FAILED', error: messageOf(error) })
    },
  )
  return () => controller.abort()
})

/**
 * Fetches the job right away (to catch up on anything missed), then every `intervalMs` if given.
 * A 404 means the server no longer has the job (jobs live in server memory, so it restarted);
 * other fetch errors are ignored: the next tick or the hub will deliver.
 */
export const watchJobActor = fromCallback<ImportEvent, { id: string; intervalMs: number | null }>(({ input, sendBack }) => {
  const controller = new AbortController()
  const fetchOnce = () =>
    getImport(input.id, controller.signal).then(
      (job) => sendBack({ type: 'JOB_UPDATED', job }),
      (error: unknown) => {
        if (error instanceof ApiError && error.status === 404) sendBack({ type: 'JOB_LOST' })
      },
    )
  void fetchOnce()
  const timer = input.intervalMs === null ? undefined : setInterval(fetchOnce, input.intervalMs)
  return () => {
    clearInterval(timer)
    controller.abort()
  }
})

export const cancelActor = fromPromise<ImportJob | null, { id: string }>(({ input }) => cancelImport(input.id))

const jobIs = (state: ImportJob['state']) => ({ context }: { context: ImportContext }) => context.job?.state === state

export const importMachine = setup({
  types: {
    context: {} as ImportContext,
    events: {} as ImportEvent,
    input: {} as ImportInput,
  },
  actors: {
    upload: uploadActor,
    watchJob: watchJobActor,
    cancelImport: cancelActor,
  },
  guards: {
    hubConnected: ({ context }) => context.hubConnected,
    hubDisconnected: ({ context }) => !context.hubConnected,
    isNewerUpdateOfOurJob: ({ context, event }) =>
      event.type === 'JOB_UPDATED' &&
      context.job !== null &&
      event.job.id === context.job.id &&
      event.job.version > context.job.version,
    hasFile: ({ context }) => context.file !== null,
    isRunning: jobIs('Running'),
    isCompleted: jobIs('Completed'),
    isDuplicate: jobIs('Duplicate'),
    isFailed: jobIs('Failed'),
    isCancelled: jobIs('Cancelled'),
  },
  actions: {
    beginUpload: assign({
      job: null,
      error: null,
      upload: ({ context }) => ({ loaded: 0, total: context.file?.size ?? 0 }),
    }),
    storeJob: assign({
      job: ({ context, event }) => (event.type === 'JOB_UPDATED' || event.type === 'UPLOAD_DONE' ? event.job : context.job),
    }),
    storeFailure: assign({
      error: ({ context }) => context.job?.failureReason ?? 'The import failed.',
    }),
  },
}).createMachine({
  id: 'import',
  context: ({ input }) => ({
    file: null,
    databaseId: null,
    job: null,
    upload: { loaded: 0, total: 0 },
    error: null,
    hubConnected: input.hubConnected ?? false,
  }),
  on: {
    HUB_STATUS: { actions: assign({ hubConnected: ({ event }) => event.connected }) },
  },
  initial: 'idle',
  states: {
    idle: {
      on: { START: { target: 'uploading', actions: assign({ file: ({ event }) => event.file, databaseId: ({ event }) => event.databaseId ?? null }) } },
    },

    uploading: {
      entry: 'beginUpload',
      invoke: { src: 'upload', input: ({ context }) => ({ file: context.file!, databaseId: context.databaseId }) },
      on: {
        UPLOAD_PROGRESS: { actions: assign({ upload: ({ event }) => ({ loaded: event.loaded, total: event.total }) }) },
        UPLOAD_DONE: { target: 'tracking', actions: 'storeJob' },
        UPLOAD_FAILED: { target: 'failed', actions: assign({ error: ({ event }) => event.error }) },
        CANCEL: 'cancelled',
      },
    },

    tracking: {
      type: 'parallel',
      // Evaluated after every change: the server job state decides when we are done.
      always: [
        { guard: 'isCompleted', target: 'completed' },
        { guard: 'isDuplicate', target: 'duplicate' },
        { guard: 'isFailed', target: 'failed', actions: 'storeFailure' },
        { guard: 'isCancelled', target: 'cancelled' },
      ],
      on: {
        JOB_UPDATED: { guard: 'isNewerUpdateOfOurJob', actions: 'storeJob' },
        JOB_LOST: {
          target: 'failed',
          actions: assign({ error: 'The server no longer knows this import; it probably restarted. Please retry.' }),
        },
      },
      states: {
        phase: {
          initial: 'queued',
          states: {
            queued: { always: { guard: 'isRunning', target: 'processing' } },
            processing: {},
          },
        },
        channel: {
          initial: 'choosing',
          states: {
            choosing: {
              always: [{ guard: 'hubConnected', target: 'live' }, { target: 'polling' }],
            },
            live: {
              // One fetch on entry: updates pushed before we knew the job id (or during an outage) are not lost.
              invoke: { src: 'watchJob', input: ({ context }) => ({ id: context.job!.id, intervalMs: null }) },
              always: { guard: 'hubDisconnected', target: 'polling' },
            },
            polling: {
              invoke: { src: 'watchJob', input: ({ context }) => ({ id: context.job!.id, intervalMs: POLL_INTERVAL_MS }) },
              always: { guard: 'hubConnected', target: 'live' },
            },
          },
        },
        request: {
          initial: 'none',
          states: {
            none: { on: { CANCEL: 'cancelling' } },
            cancelling: {
              invoke: {
                src: 'cancelImport',
                input: ({ context }) => ({ id: context.job!.id }),
                onDone: {
                  target: 'none',
                  // A queued job is cancelled at once; a running one reports Cancelled through updates.
                  actions: assign({
                    job: ({ context, event }) =>
                      event.output && context.job && event.output.version > context.job.version ? event.output : context.job,
                  }),
                },
                onError: { target: 'none', actions: assign({ error: 'The cancel request failed.' }) },
              },
            },
          },
        },
      },
    },

    completed: { on: { START: { target: 'uploading', actions: assign({ file: ({ event }) => event.file }) }, RESET: 'idle' } },
    duplicate: { on: { START: { target: 'uploading', actions: assign({ file: ({ event }) => event.file }) }, RESET: 'idle' } },
    cancelled: { on: { START: { target: 'uploading', actions: assign({ file: ({ event }) => event.file }) }, RESET: 'idle' } },
    failed: {
      on: {
        RETRY: { guard: 'hasFile', target: 'uploading' },
        START: { target: 'uploading', actions: assign({ file: ({ event }) => event.file }) },
        RESET: 'idle',
      },
    },
  },
})

export type ImportSnapshot = SnapshotFrom<typeof importMachine>

export type ImportStatus =
  | 'idle'
  | 'uploading'
  | 'queued'
  | 'processing'
  | 'cancelling'
  | 'completed'
  | 'duplicate'
  | 'failed'
  | 'cancelled'

/** The single status the UI shows for a machine snapshot. */
export function importStatus(snapshot: ImportSnapshot): ImportStatus {
  if (snapshot.matches({ tracking: { request: 'cancelling' } })) return 'cancelling'
  if (snapshot.matches({ tracking: { phase: 'processing' } })) return 'processing'
  if (snapshot.matches('tracking')) return 'queued'
  const value = snapshot.value
  return typeof value === 'string' ? (value as ImportStatus) : 'idle'
}

/** Upload and server-side progress as 0–100. */
export function importProgress(context: ImportContext) {
  const pct = (part: number, whole: number) => (whole > 0 ? Math.min(100, Math.round((part / whole) * 100)) : 0)
  return {
    uploadPercent: pct(context.upload.loaded, context.upload.total),
    processPercent: context.job ? pct(context.job.bytesRead, context.job.totalBytes) : 0,
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'The upload failed.'
}
