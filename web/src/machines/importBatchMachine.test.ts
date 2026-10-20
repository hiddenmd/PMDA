import { describe, expect, it } from 'vitest'
import { createActor, fromCallback, fromPromise } from 'xstate'
import type { ImportJob } from '../api/types'
import { makeFile, makeJob } from '../test/fixtures'
import { batchSummary, importBatchMachine, MAX_UPLOADS, type FileImportRef } from './importBatchMachine'
import { importMachine, importStatus, type ImportEvent } from './importMachine'

interface FakeUpload {
  name: string
  sendBack: (e: ImportEvent) => void
  aborted: boolean
}

function harness() {
  const uploads: FakeUpload[] = []
  const cancels: string[] = []
  const file = importMachine.provide({
    actors: {
      upload: fromCallback<ImportEvent, { file: File; databaseId: string | null }>(({ input, sendBack }) => {
        const upload = { name: input.file.name, sendBack, aborted: false }
        uploads.push(upload)
        return () => {
          upload.aborted = true
        }
      }),
      watchJob: fromCallback(() => () => undefined),
      cancelImport: fromPromise<ImportJob | null, { id: string }>(async ({ input }) => {
        cancels.push(input.id)
        return null
      }),
    },
  })
  const actor = createActor(importBatchMachine.provide({ actors: { importFile: file } }), { input: { hubConnected: true } }).start()

  const child = (name: string) => {
    const item = actor.getSnapshot().context.items.find((i) => i.file.name === name)!
    return actor.getSnapshot().children[item.id] as FileImportRef
  }
  const status = (name: string) => importStatus(child(name).getSnapshot())
  const upload = (name: string) => uploads.find((u) => u.name === name)!
  const add = (...names: string[]) => actor.send({ type: 'ADD_FILES', files: names.map((n) => makeFile(n)) })

  return { actor, uploads, cancels, child, status, upload, add, summary: () => batchSummary(actor.getSnapshot()) }
}

describe('importBatchMachine', () => {
  it(`uploads at most ${MAX_UPLOADS} files at once and starts the next when one finishes uploading`, () => {
    const h = harness()
    h.add('a.bin', 'b.bin', 'c.bin', 'd.bin')

    expect(h.uploads.map((u) => u.name)).toEqual(['a.bin', 'b.bin'])
    expect(h.summary()).toMatchObject({ total: 4, uploading: 2, waiting: 2 })

    h.upload('a.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-a', fileName: 'a.bin' }) })

    expect(h.status('a.bin')).toBe('queued')
    expect(h.uploads.map((u) => u.name)).toEqual(['a.bin', 'b.bin', 'c.bin'])

    h.upload('b.bin').sendBack({ type: 'UPLOAD_FAILED', error: 'Network error' }) // a failure frees its slot too
    expect(h.uploads.map((u) => u.name)).toEqual(['a.bin', 'b.bin', 'c.bin', 'd.bin'])
    expect(h.summary()).toMatchObject({ uploading: 2, importing: 1, failed: 1, waiting: 0 })
  })

  it('files added later join the queue behind the others', () => {
    const h = harness()
    h.add('a.bin', 'b.bin', 'c.bin')
    h.add('d.bin')

    expect(h.uploads.map((u) => u.name)).toEqual(['a.bin', 'b.bin'])
    h.upload('a.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-a' }) })
    expect(h.uploads.at(-1)!.name).toBe('c.bin')
  })

  it('routes job updates to the file that owns the job', () => {
    const h = harness()
    h.add('a.bin', 'b.bin')
    h.upload('a.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-a' }) })
    h.upload('b.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-b' }) })

    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ id: 'job-b', version: 2, state: 'Completed', flightId: 'f-b' }) })

    expect(h.status('a.bin')).toBe('queued')
    expect(h.status('b.bin')).toBe('completed')
    expect(h.summary()).toMatchObject({ importing: 1, completed: 1 })
  })

  it('cancels a waiting file by dropping it and a running one through the server', async () => {
    const h = harness()
    h.add('a.bin', 'b.bin', 'c.bin')
    h.upload('a.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-a' }) })
    // a queued on the server, b uploading, c uploading (slot freed by a)
    h.add('d.bin')
    const waiting = h.actor.getSnapshot().context.items.find((i) => i.file.name === 'd.bin')!

    h.actor.send({ type: 'CANCEL', id: waiting.id })
    expect(h.actor.getSnapshot().context.items.map((i) => i.file.name)).toEqual(['a.bin', 'b.bin', 'c.bin'])

    h.actor.send({ type: 'CANCEL_ALL' })
    expect(h.upload('b.bin').aborted).toBe(true)
    expect(h.summary()).toMatchObject({ total: 3, cancelled: 2, importing: 1 }) // a: cancel request on its way to the server
    expect(h.status('b.bin')).toBe('cancelled')
    await Promise.resolve()
    expect(h.cancels).toEqual(['job-a'])
  })

  it('clears finished files and keeps the rest', () => {
    const h = harness()
    h.add('a.bin', 'b.bin', 'c.bin')
    h.upload('a.bin').sendBack({ type: 'UPLOAD_DONE', job: makeJob({ id: 'job-a' }) })
    h.actor.send({ type: 'JOB_UPDATED', job: makeJob({ id: 'job-a', version: 2, state: 'Completed' }) })
    h.upload('b.bin').sendBack({ type: 'UPLOAD_FAILED', error: 'boom' })

    h.actor.send({ type: 'CLEAR_FINISHED' })

    expect(h.actor.getSnapshot().context.items.map((i) => i.file.name)).toEqual(['c.bin'])
    expect(Object.keys(h.actor.getSnapshot().children)).toHaveLength(1)
  })

  it('retries a failed file', () => {
    const h = harness()
    h.add('a.bin')
    h.upload('a.bin').sendBack({ type: 'UPLOAD_FAILED', error: 'boom' })
    const id = h.actor.getSnapshot().context.items[0].id

    h.actor.send({ type: 'RETRY', id })

    expect(h.status('a.bin')).toBe('uploading')
    expect(h.uploads).toHaveLength(2)
  })
})
