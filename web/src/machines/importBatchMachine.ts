import { enqueueActions, setup, stopChild, type ActorRefFrom, type SnapshotFrom } from 'xstate'
import type { ImportJob } from '../api/types'
import { importMachine, importStatus, type ImportSnapshot, type ImportStatus } from './importMachine'

/**
 * A batch of file imports. Each file is driven by its own importMachine, spawned as child `file-<n>`;
 * the parent hears every change of it as `xstate.snapshot.file-<n>` and keeps the file's status in
 * its own context (so the summary is derived from one place). At most MAX_UPLOADS files upload at
 * once: uploading everything together would make every file finish uploading late, while one or two
 * at a time lets the server import the first files while the rest upload. The server queues and runs
 * the imports themselves.
 *
 *   waiting ─(upload slot free)→ starting ─(child took START)→ uploading → … the child's lifecycle
 */

export const MAX_UPLOADS = 2

/** `waiting` for an upload slot, `starting` once START is sent, then the file's own import status. */
export type FileStatus = 'waiting' | 'starting' | Exclude<ImportStatus, 'idle'>

export interface BatchItem {
  id: string
  file: File
  /** The flight database chosen when the file was added (null: the server's default). */
  databaseId: string | null
  status: FileStatus
}

export interface BatchContext {
  items: BatchItem[]
  nextId: number
  hubConnected: boolean
}

export type BatchEvent =
  | { type: 'ADD_FILES'; files: File[]; databaseId?: string | null }
  | { type: 'CANCEL'; id: string }
  | { type: 'CANCEL_ALL' }
  | { type: 'RETRY'; id: string }
  | { type: 'REMOVE'; id: string }
  | { type: 'CLEAR_FINISHED' }
  | { type: 'HUB_STATUS'; connected: boolean }
  | { type: 'JOB_UPDATED'; job: ImportJob }

export type FileImportRef = ActorRefFrom<typeof importMachine>

const FINISHED: readonly FileStatus[] = ['completed', 'duplicate', 'failed', 'cancelled']
export const isFinished = (status: FileStatus) => FINISHED.includes(status)
const holdsUploadSlot = (status: FileStatus) => status === 'starting' || status === 'uploading'
const SNAPSHOT_PREFIX = 'xstate.snapshot.'

export const importBatchMachine = setup({
  types: {
    context: {} as BatchContext,
    events: {} as BatchEvent,
    input: {} as { hubConnected?: boolean },
  },
  actors: { importFile: importMachine },
  actions: {
    /** Starts waiting files, in order, while upload slots are free. */
    pump: enqueueActions(({ context, enqueue }) => {
      let free = MAX_UPLOADS - context.items.filter((i) => holdsUploadSlot(i.status)).length
      const starting = new Set<string>()
      for (const item of context.items) {
        if (free <= 0) break
        if (item.status !== 'waiting') continue
        enqueue.sendTo(item.id, { type: 'START', file: item.file, databaseId: item.databaseId })
        starting.add(item.id)
        free--
      }
      if (starting.size > 0) {
        enqueue.assign({ items: ({ context }) => context.items.map((i) => (starting.has(i.id) ? { ...i, status: 'starting' as const } : i)) })
      }
    }),
    /** Drops files that are finished (or never started) and stops their machines. */
    removeItems: enqueueActions(({ context, enqueue }, params: { id?: string }) => {
      const remove = context.items.filter(
        (i) => (params.id === undefined || i.id === params.id) && (isFinished(i.status) || (params.id !== undefined && i.status === 'waiting')),
      )
      for (const item of remove) enqueue(stopChild(item.id))
      const ids = new Set(remove.map((i) => i.id))
      if (ids.size > 0) enqueue.assign({ items: ({ context }) => context.items.filter((i) => !ids.has(i.id)) })
    }),
  },
}).createMachine({
  id: 'importBatch',
  context: ({ input }) => ({ items: [], nextId: 1, hubConnected: input.hubConnected ?? false }),
  on: {
    ADD_FILES: {
      actions: [
        enqueueActions(({ context, event, enqueue }) => {
          const added = event.files.map((file, i): BatchItem => ({ id: `file-${context.nextId + i}`, file, databaseId: event.databaseId ?? null, status: 'waiting' }))
          for (const item of added) {
            enqueue.spawnChild('importFile', { id: item.id, input: { hubConnected: context.hubConnected }, syncSnapshot: true })
          }
          enqueue.assign({ items: [...context.items, ...added], nextId: context.nextId + added.length })
        }),
        'pump',
      ],
    },
    // A file's machine changed: record its status if it moved on (progress ticks change nothing here), then maybe start the next file.
    'xstate.snapshot.*': {
      actions: [
        enqueueActions(({ context, event, enqueue }) => {
          const id = event.type.slice(SNAPSHOT_PREFIX.length)
          const status = importStatus((event as unknown as { snapshot: ImportSnapshot }).snapshot)
          const item = context.items.find((i: BatchItem) => i.id === id)
          if (!item || status === 'idle' || item.status === status) return
          enqueue.assign({ items: ({ context }: { context: BatchContext }) => context.items.map((i) => (i.id === id ? { ...i, status } : i)) })
        }),
        'pump',
      ],
    },
    CANCEL: {
      actions: enqueueActions(({ context, event, enqueue }) => {
        const item = context.items.find((i) => i.id === event.id)
        if (!item) return
        // A waiting file never reached the server: just drop it.
        if (item.status === 'waiting') enqueue({ type: 'removeItems', params: { id: item.id } })
        else enqueue.sendTo(item.id, { type: 'CANCEL' })
      }),
    },
    CANCEL_ALL: {
      actions: enqueueActions(({ context, enqueue }) => {
        for (const item of context.items) {
          if (item.status === 'waiting') enqueue({ type: 'removeItems', params: { id: item.id } })
          else if (!isFinished(item.status)) enqueue.sendTo(item.id, { type: 'CANCEL' })
        }
      }),
    },
    RETRY: {
      actions: enqueueActions(({ context, event, enqueue }) => {
        if (context.items.find((i) => i.id === event.id)?.status === 'failed') enqueue.sendTo(event.id, { type: 'RETRY' })
      }),
    },
    REMOVE: { actions: { type: 'removeItems', params: ({ event }) => ({ id: event.id }) } },
    CLEAR_FINISHED: { actions: { type: 'removeItems', params: {} } },
    HUB_STATUS: {
      actions: enqueueActions(({ context, event, enqueue }) => {
        enqueue.assign({ hubConnected: event.connected })
        for (const item of context.items) enqueue.sendTo(item.id, { type: 'HUB_STATUS', connected: event.connected })
      }),
    },
    // Each file's machine ignores updates for jobs that are not its own.
    JOB_UPDATED: {
      actions: enqueueActions(({ context, event, enqueue }) => {
        for (const item of context.items) {
          if (item.status !== 'waiting' && !isFinished(item.status)) enqueue.sendTo(item.id, { type: 'JOB_UPDATED', job: event.job })
        }
      }),
    },
  },
})

export type ImportBatchSnapshot = SnapshotFrom<typeof importBatchMachine>

export interface BatchSummary {
  total: number
  waiting: number
  uploading: number
  importing: number
  completed: number
  duplicate: number
  failed: number
  cancelled: number
}

/** Counts per state, for the batch header. */
export function batchSummary(snapshot: ImportBatchSnapshot): BatchSummary {
  const s: BatchSummary = { total: 0, waiting: 0, uploading: 0, importing: 0, completed: 0, duplicate: 0, failed: 0, cancelled: 0 }
  for (const { status } of snapshot.context.items) {
    s.total++
    switch (status) {
      case 'waiting':
      case 'starting':
        s.waiting++
        break
      case 'uploading':
        s.uploading++
        break
      case 'queued':
      case 'processing':
      case 'cancelling':
        s.importing++
        break
      default:
        s[status]++
    }
  }
  return s
}
