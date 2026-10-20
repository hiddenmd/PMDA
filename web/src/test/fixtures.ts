import type { ImportJob } from '../api/types'

export function makeJob(overrides: Partial<ImportJob> = {}): ImportJob {
  return {
    id: 'job-1',
    version: 1,
    fileName: 'sortie.bin',
    sizeBytes: 100,
    state: 'Queued',
    createdUtc: '2026-10-17T08:00:00Z',
    startedUtc: null,
    finishedUtc: null,
    bytesRead: 0,
    totalBytes: 100,
    eventCount: 0,
    errorCount: 0,
    flightId: null,
    failureReason: null,
    databaseId: 'main',
    databaseName: 'Main',
    ...overrides,
  }
}

export const makeFile = (name = 'sortie.bin', size = 100) => new File([new Uint8Array(size)], name)
