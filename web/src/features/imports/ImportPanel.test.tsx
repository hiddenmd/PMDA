import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ImportViewModel } from '../../hooks/useImport'
import { makeFile, makeJob } from '../../test/fixtures'
import { ImportPanelView } from './ImportPanel'

function renderPanel(overrides: Partial<ImportViewModel> = {}) {
  const vm: ImportViewModel & { onOpenFlight: (id: string) => void } = {
    status: 'idle',
    file: null,
    job: null,
    error: null,
    uploadPercent: 0,
    processPercent: 0,
    start: vi.fn(),
    cancel: vi.fn(),
    retry: vi.fn(),
    reset: vi.fn(),
    onOpenFlight: vi.fn(),
    ...overrides,
  }
  render(<ImportPanelView {...vm} />)
  return vm
}

describe('ImportPanelView', () => {
  it('starts an import with the chosen file', async () => {
    const vm = renderPanel()
    const file = makeFile('sortie-7.bin')

    await userEvent.upload(screen.getByLabelText('Flight recording'), file)

    expect(vm.start).toHaveBeenCalledWith(file)
  })

  it('shows upload progress and can cancel', async () => {
    const vm = renderPanel({ status: 'uploading', file: makeFile('a.bin'), uploadPercent: 40 })

    expect(screen.getByText('Uploading a.bin')).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Upload' })).toHaveAttribute('aria-valuenow', '40')
    expect(screen.queryByLabelText('Flight recording')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(vm.cancel).toHaveBeenCalledOnce()
  })

  it('shows parsing progress with counts', () => {
    renderPanel({
      status: 'processing',
      job: makeJob({ state: 'Running', eventCount: 12345, errorCount: 2 }),
      processPercent: 70,
    })

    expect(screen.getByRole('progressbar', { name: 'Parsing' })).toHaveAttribute('aria-valuenow', '70')
    expect(screen.getByText(/12,345 events · 2 problems/)).toBeInTheDocument()
  })

  it('offers to open the imported flight', async () => {
    const vm = renderPanel({ status: 'completed', job: makeJob({ state: 'Completed', flightId: 'f-9', eventCount: 10 }) })

    await userEvent.click(screen.getByRole('button', { name: 'Open flight' }))

    expect(vm.onOpenFlight).toHaveBeenCalledWith('f-9')
    expect(screen.getByLabelText('Flight recording')).toBeInTheDocument() // can import the next one
  })

  it('shows the failure reason and retries', async () => {
    const vm = renderPanel({ status: 'failed', file: makeFile('bad.bin'), error: 'Invalid file header' })

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid file header')
    await userEvent.click(screen.getByRole('button', { name: 'Retry bad.bin' }))

    expect(vm.retry).toHaveBeenCalledOnce()
  })
})
