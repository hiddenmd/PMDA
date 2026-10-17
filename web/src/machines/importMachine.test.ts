import { describe, expect, it } from 'vitest'
import { createActor, fromCallback, fromPromise, waitFor } from 'xstate'
import type { ImportJob } from '../api/types'
import { makeFile, makeJob } from '../test/fixtures'
import { importMachine, importProgress, importStatus, POLL_INTERVAL_MS, type ImportEvent } from './importMachine'

interface FakeUpload {
  file: File
  sendBack: (e: ImportEvent) => void
  aborted: boolean
}
interface FakeWatch {
  id: string
  intervalMs: number | null
  sendBack: (e: ImportEvent) => void
  stopped: boolean
}

function harness({ hubConnected = true, cancelResult = null as ImportJob | null } = {}) {
  const uploads: FakeUpload[] = []
  const watches: FakeWatch[] = []
  const cancels: string[] = []
  let resolveCancel: (job: ImportJob | null) => void = () => undefined

  const machine = importMachine.provide({
    actors: {
      upload: fromCallback<ImportEvent, { file: File }>(({ input, sendBack }) => {
        const upload: FakeUpload = { file: input.file, sendBack, aborted: false }
        uploads.push(upload)
        return () => {
          upload.aborted = true
        }
      }),
      watchJob: fromCallback<ImportEvent, { id: string; intervalMs: number | null }>(({ input, sendBack }) => {
        const watch: FakeWatch = { ...input, sendBack, stopped: false }
        watches.push(watch)
        return () => {
          watch.stopped = true
        }
      }),
      cancelImport: fromPromise<ImportJob | null, { id: string }>(({ input }) => {
        cancels.push(input.id)
        return new Promise((resolve) => {
          resolveCancel = resolve
        })
      }),
    },
  })

  const actor = createActor(machine, { input: { hubConnected } }).start()
  const status = () => importStatus(actor.getSnapshot())
  const activeWatches = () => watches.filter((w) => !w.stopped)
  const finishCancel = async (job: ImportJob | null = cancelResult) => {
    resolveCancel(job)
    await waitFor(actor, (s) => !s.matches({ tracking: { request: 'cancelling' } }))
  }

  /** Start an upload and let it succeed with a queued job. */
  const uploaded = (job = makeJob()) => {
    actor.send({ type: 'START', file: makeFile() })
    uploads.at(-1)!.sendBack({ type: 'UPLOAD_DONE', job })
  }

  return { actor, uploads, watches, cancels, status, activeWatches, finishCancel, uploaded }
}

describe('importMachine', () => {
  it('goes idle → uploading → queued → processing → completed with live updates', () => {
    const h = harness()
    expect(h.status()).toBe('idle')

    h.actor.send({ type: 'START', file: makeFile('a.bin', 200) })
    expect(h.status()).toBe('uploading')
    expect(h.uploads[0].file.name).toBe('a.bin')

    h.uploads[0].sendBack({ type: 'UPLOAD_PROGRESS', loaded: 100, total: 200 })
    expect(importProgress(h.actor.getSnapshot().context).uploadPercent).toBe(50)

    h.uploads[0].sendBack({ type: 'UPLOAD_DONE', job: makeJob() })
    expect(h.status()).toBe('queued')
    expect(h.activeWatches()).toEqual([expect.objectContaining({ id: 'job-1', intervalMs: null })])

    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 2, state: 'Running', bytesRead: 25 }) })
    expect(h.status()).toBe('processing')
    expect(importProgress(h.actor.getSnapshot().context).processPercent).toBe(25)

    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 3, state: 'Completed', flightId: 'f-1' }) })
    expect(h.status()).toBe('completed')
    expect(h.actor.getSnapshot().context.job?.flightId).toBe('f-1')
    expect(h.activeWatches()).toEqual([])
  })

  it('ignores updates for other jobs and stale versions', () => {
    const h = harness()
    h.uploaded(makeJob({ version: 5 }))

    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ id: 'other', version: 9, state: 'Completed' }) })
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 4, state: 'Failed' }) })
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 5, state: 'Running' }) })

    expect(h.status()).toBe('queued')
    expect(h.actor.getSnapshot().context.job?.version).toBe(5)
  })

  it('catches up on updates pushed before the upload response arrived', () => {
    const h = harness()
    h.uploaded()

    // The live channel's entry fetch returns the job, already finished on the server.
    h.activeWatches()[0].sendBack({ type: 'JOB_UPDATED', job: makeJob({ version: 4, state: 'Completed' }) })

    expect(h.status()).toBe('completed')
  })

  it('polls while the hub is offline and switches back when it reconnects', () => {
    const h = harness({ hubConnected: false })
    h.uploaded()
    expect(h.activeWatches()).toEqual([expect.objectContaining({ intervalMs: POLL_INTERVAL_MS })])

    h.actor.send({ type: 'HUB_STATUS', connected: true })
    expect(h.activeWatches()).toEqual([expect.objectContaining({ intervalMs: null })])

    h.actor.send({ type: 'HUB_STATUS', connected: false })
    expect(h.activeWatches()).toEqual([expect.objectContaining({ intervalMs: POLL_INTERVAL_MS })])

    h.activeWatches()[0].sendBack({ type: 'JOB_UPDATED', job: makeJob({ version: 2, state: 'Running' }) })
    expect(h.status()).toBe('processing')
  })

  it('cancelling during upload aborts it', () => {
    const h = harness()
    h.actor.send({ type: 'START', file: makeFile() })

    h.actor.send({ type: 'CANCEL' })

    expect(h.status()).toBe('cancelled')
    expect(h.uploads[0].aborted).toBe(true)
  })

  it('a late upload result after cancel is ignored', () => {
    const h = harness()
    h.actor.send({ type: 'START', file: makeFile() })
    h.actor.send({ type: 'CANCEL' })

    h.uploads[0].sendBack({ type: 'UPLOAD_DONE', job: makeJob() })

    expect(h.status()).toBe('cancelled')
  })

  it('cancelling a queued job ends cancelled from the response', async () => {
    const h = harness()
    h.uploaded()

    h.actor.send({ type: 'CANCEL' })
    expect(h.status()).toBe('cancelling')
    expect(h.cancels).toEqual(['job-1'])

    await h.finishCancel(makeJob({ version: 2, state: 'Cancelled' }))
    expect(h.status()).toBe('cancelled')
  })

  it('cancelling a running job waits for the Cancelled update', async () => {
    const h = harness()
    h.uploaded()
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 2, state: 'Running' }) })

    h.actor.send({ type: 'CANCEL' })
    await h.finishCancel(makeJob({ version: 2, state: 'Running' }))
    expect(h.status()).toBe('processing')

    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 3, state: 'Cancelled' }) })
    expect(h.status()).toBe('cancelled')
  })

  it('a failed job shows the server reason and can be retried with the same file', () => {
    const h = harness()
    h.uploaded()
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 2, state: 'Failed', failureReason: 'Bad header' }) })

    expect(h.status()).toBe('failed')
    expect(h.actor.getSnapshot().context.error).toBe('Bad header')

    h.actor.send({ type: 'RETRY' })
    expect(h.status()).toBe('uploading')
    expect(h.uploads).toHaveLength(2)
    expect(h.uploads[1].file).toBe(h.uploads[0].file)
    expect(h.actor.getSnapshot().context.error).toBeNull()
  })

  it('an upload error fails with its message', () => {
    const h = harness()
    h.actor.send({ type: 'START', file: makeFile() })

    h.uploads[0].sendBack({ type: 'UPLOAD_FAILED', error: 'The import queue is full.' })

    expect(h.status()).toBe('failed')
    expect(h.actor.getSnapshot().context.error).toBe('The import queue is full.')
  })

  it('a duplicate ends in duplicate and a new file can be started from there', () => {
    const h = harness()
    h.uploaded()
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ version: 2, state: 'Duplicate' }) })
    expect(h.status()).toBe('duplicate')

    h.actor.send({ type: 'START', file: makeFile('next.bin') })
    expect(h.status()).toBe('uploading')
    expect(h.uploads[1].file.name).toBe('next.bin')
  })

  it('reset returns to idle', () => {
    const h = harness()
    h.actor.send({ type: 'START', file: makeFile() })
    h.actor.send({ type: 'CANCEL' })

    h.actor.send({ type: 'RESET' })

    expect(h.status()).toBe('idle')
  })
})
