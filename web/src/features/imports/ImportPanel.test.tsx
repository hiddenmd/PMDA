import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useActorRef } from '@xstate/react'
import { useEffect } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { fromCallback, fromPromise } from 'xstate'
import type { DatabaseChoice } from '../../api/databases'
import type { FormatSummary } from '../../api/formats'
import type { ImportJob } from '../../api/types'
import { useBatchView } from '../../hooks/useImportBatch'
import { importBatchMachine } from '../../machines/importBatchMachine'
import { importMachine, type ImportEvent } from '../../machines/importMachine'
import { makeFile, makeJob } from '../../test/fixtures'
import { ImportPanelView } from './ImportPanel'

/** The real batch and file machines, with uploads and the server faked. */
function renderPanel(targets: DatabaseChoice[] = [], defaultTarget: string | null = null, formats: FormatSummary[] = []) {
  const uploads = new Map<string, (e: ImportEvent) => void>()
  const targetOf = new Map<string, string | null>()
  const formatOf = new Map<string, string | null>()
  const onOpenFlight = vi.fn()
  let send: (e: { type: 'JOB_UPDATED'; job: ImportJob }) => void = () => undefined

  const machine = importBatchMachine.provide({
    actors: {
      importFile: importMachine.provide({
        actors: {
          upload: fromCallback<ImportEvent, { file: File; databaseId: string | null; format?: string | null }>(({ input, sendBack }) => {
            uploads.set(input.file.name, sendBack)
            targetOf.set(input.file.name, input.databaseId)
            formatOf.set(input.file.name, input.format ?? null)
            return () => uploads.delete(input.file.name)
          }),
          watchJob: fromCallback(() => () => undefined),
          cancelImport: fromPromise<ImportJob | null, { id: string }>(async () => null),
        },
      }),
    },
  })

  function Harness() {
    const actor = useActorRef(machine, { input: { hubConnected: true } })
    useEffect(() => {
      send = actor.send
    }, [actor])
    return <ImportPanelView batch={useBatchView(actor)} onOpenFlight={onOpenFlight} targets={targets} defaultTarget={defaultTarget} formats={formats} />
  }
  render(<Harness />)

  const uploaded = (name: string, job: ImportJob) => act(() => uploads.get(name)!({ type: 'UPLOAD_DONE', job }))
  const update = (job: ImportJob) => act(() => send({ type: 'JOB_UPDATED', job }))
  const row = (name: string) => screen.getByText(name).closest('li')!
  return { uploads, targetOf, formatOf, onOpenFlight, uploaded, update, row }
}

describe('ImportPanelView', () => {
  it('offers the header-defined formats and sends the chosen one with the upload', async () => {
    const formats: FormatSummary[] = [
      { id: 'PROJ-A', name: 'Project A', version: 3, canImport: true, messages: 12, errors: 0, warnings: 1 },
      { id: 'PROJ-B', name: 'Project B', version: null, canImport: false, messages: 0, errors: 0, warnings: 0 },
    ]
    const h = renderPanel([], null, formats)
    const picker = screen.getByLabelText('Format')
    expect(within(picker).getByRole('option', { name: 'Project B (not ready)' })).toBeDisabled()

    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('auto.raw')])
    await userEvent.selectOptions(picker, 'PROJ-A')
    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('a.raw')])

    expect(h.formatOf.get('auto.raw')).toBeNull()
    expect(h.formatOf.get('a.raw')).toBe('PROJ-A')
  })

  it('shows no format picker when there are no header-defined formats', () => {
    renderPanel()
    expect(screen.queryByLabelText('Format')).not.toBeInTheDocument()
  })

  it('takes several files at once, uploading two while the rest wait', async () => {
    const h = renderPanel()

    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('a.bin'), makeFile('b.bin'), makeFile('c.bin')])

    expect([...h.uploads.keys()]).toEqual(['a.bin', 'b.bin'])
    expect(within(h.row('c.bin')).getByText('Waiting to upload')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('3 files: 2 uploading · 1 waiting')
  })

  it('shows each file through import to an Open button', async () => {
    const h = renderPanel()
    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('a.bin')])

    h.uploaded('a.bin', makeJob({ id: 'job-a', fileName: 'a.bin' }))
    expect(within(h.row('a.bin')).getByText('Queued on the server')).toBeInTheDocument()

    h.update(makeJob({ id: 'job-a', version: 2, state: 'Running', bytesRead: 70, totalBytes: 100, eventCount: 12_345 }))
    expect(within(h.row('a.bin')).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '70')
    expect(within(h.row('a.bin')).getByText(/Importing 70% · 12\.3K events/)).toBeInTheDocument()

    h.update(makeJob({ id: 'job-a', version: 3, state: 'Completed', flightId: 'f-9', eventCount: 12_345, errorCount: 2 }))
    expect(within(h.row('a.bin')).getByText('Imported · 12,345 events · 2 problems')).toBeInTheDocument()
    await userEvent.click(within(h.row('a.bin')).getByRole('button', { name: 'Open' }))
    expect(h.onOpenFlight).toHaveBeenCalledWith('f-9')
  })

  it('shows a failure on its row, retries it, and clears finished rows', async () => {
    const h = renderPanel()
    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('bad.bin'), makeFile('good.bin')])

    act(() => h.uploads.get('bad.bin')!({ type: 'UPLOAD_FAILED', error: 'Invalid file header' }))
    expect(within(h.row('bad.bin')).getByText('Invalid file header')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Retry bad.bin' }))
    expect(h.uploads.has('bad.bin')).toBe(true)

    act(() => h.uploads.get('bad.bin')!({ type: 'UPLOAD_FAILED', error: 'Invalid file header' }))
    await userEvent.click(screen.getByRole('button', { name: 'Clear finished' }))
    expect(screen.queryByText('bad.bin')).not.toBeInTheDocument()
    expect(screen.getByText('good.bin')).toBeInTheDocument()
  })

  it('imports into the chosen database, preset to the default', async () => {
    const h = renderPanel(
      [
        { id: 'main', name: 'Main', readOnly: false },
        { id: 'bravo', name: 'Bravo', readOnly: false },
      ],
      'main',
    )
    const picker = screen.getByLabelText('Import into')
    expect(picker).toHaveValue('main')

    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('a.bin')])
    await userEvent.selectOptions(picker, 'bravo')
    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('b.bin')])

    expect(h.targetOf.get('a.bin')).toBe('main')
    expect(h.targetOf.get('b.bin')).toBe('bravo')
  })

  it('has no picker with a single database', () => {
    renderPanel([{ id: 'main', name: 'Main', readOnly: false }], 'main')
    expect(screen.queryByLabelText('Import into')).not.toBeInTheDocument()
  })

  it('cancels everything still in progress', async () => {
    const h = renderPanel()
    await userEvent.upload(screen.getByLabelText('Flight recordings'), [makeFile('a.bin'), makeFile('b.bin'), makeFile('c.bin')])

    await userEvent.click(screen.getByRole('button', { name: 'Cancel all' }))

    expect(h.uploads.size).toBe(0) // uploads aborted
    expect(screen.queryByText('c.bin')).not.toBeInTheDocument() // never started: dropped
    expect(screen.getByRole('status')).toHaveTextContent('2 files: 2 cancelled')
  })
})
