import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { FormatDetail, FormatSummary, FormatUploadResult } from '../../api/formats'
import { AuthContext } from '../../auth/authContext'
import { FormatsPanel } from './FormatsPanel'

const listFormats = vi.fn()
const getFormat = vi.fn()
const uploadHeader = vi.fn()
const saveFormat = vi.fn()
vi.mock('../../api/formats', async (original) => ({
  ...(await original<typeof import('../../api/formats')>()),
  listFormats: (...a: unknown[]) => listFormats(...a),
  getFormat: (...a: unknown[]) => getFormat(...a),
  uploadHeader: (...a: unknown[]) => uploadHeader(...a),
  saveFormat: (...a: unknown[]) => saveFormat(...a),
}))

const summaries: FormatSummary[] = [{ id: 'DEMO-H', name: 'Demo project (.h)', version: 2, canImport: true, messages: 6, errors: 0, warnings: 1 }]

const detail: FormatDetail = {
  project: { id: 'DEMO-H', name: 'Demo project (.h)' },
  version: 2,
  versions: [
    { version: 1, sha256: 'AA'.repeat(32), bytes: 9000, savedUtc: '2026-10-20T10:00:00Z' },
    { version: 2, sha256: 'BB'.repeat(32), bytes: 9100, savedUtc: '2026-10-20T11:00:00Z' },
  ],
  canImport: true,
  sqlSchema: 'DEMO_H',
  frame: { headerBytes: 16, sync: 'A5A5A5A5', length: 'DataOnly', lengthUnitBytes: 1 },
  diagnostics: [{ line: 120, severity: 'Warning', message: 'nav_struct.mode: <Enum>NAV_MODE is not defined anywhere; the values show as numbers.' }],
  messages: [
    {
      key: 'threat_struct',
      table: 'threat_struct',
      events: [300],
      stored: true,
      tracked: true,
      title: 'Threats',
      group: 'Threats.Event',
      level: 4,
      hidden: false,
      sizeBytes: 412,
      columns: [{ field: 'aoa', name: 'aoa', type: 'Float', title: 'AOA', unit: 'deg', enum: null }],
      children: [{ field: 'pris', table: 'threat_struct_pris', countField: 'pri_count', maxCount: 32, columns: [] }],
    },
    { key: 'threat_drop_struct', table: 'threat_drop_struct', events: [5], stored: false, tracked: true, title: null, group: null, level: null, hidden: false, sizeBytes: 16, columns: [], children: [] },
  ],
  enums: 9,
  eventProperties: ['EmitterId', 'FrequencyMHz'],
}

function renderPanel() {
  const auth = { user: { id: 'u', userName: 'admin', displayName: 'Admin', role: 'Admin' as const }, isAdmin: true, signOut: vi.fn() }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}>
      <AuthContext.Provider value={auth}>
        <FormatsPanel />
      </AuthContext.Provider>
    </QueryClientProvider>,
  )
}

describe('FormatsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listFormats.mockResolvedValue(summaries)
    getFormat.mockResolvedValue(detail)
  })

  it('lists projects and shows the first one with its messages and tables', async () => {
    renderPanel()
    expect(await screen.findByText('v2')).toBeInTheDocument()
    const row = (await screen.findByText('Threats')).closest('tr')!
    expect(screen.queryByText('threat_struct_pris')).not.toBeInTheDocument() // columns stay folded
    expect(within(row).getByText('300')).toBeInTheDocument()
    expect(screen.getByText('merged')).toBeInTheDocument() // delete events feed the track, no table of their own

    await userEvent.click(within(row).getByRole('button', { name: /threat_struct/ }))
    expect(screen.getByText('deg')).toBeInTheDocument()
    expect(screen.getByText('threat_struct_pris')).toBeInTheDocument()
  })

  it('uploads a new header version and shows what moved', async () => {
    const result: FormatUploadResult = {
      detail: { ...detail, version: 3 },
      newVersion: true,
      diff: { addedMessages: [], removedMessages: [], changedStructs: [{ struct: 'nav_struct', sizeBefore: 44, sizeAfter: 44, added: ['valid_ins'], removed: [], moved: ['valid_pos', 'valid_att'] }] },
    }
    uploadHeader.mockResolvedValue(result)
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Header' }))
    expect(screen.getByText(/NAV_MODE is not defined/)).toBeInTheDocument()

    await userEvent.upload(screen.getByLabelText('Header file'), new File(['struct x {};'], 'v3.h', { type: 'text/plain' }))
    expect(uploadHeader).toHaveBeenCalledWith('DEMO-H', expect.any(File))
    expect(await screen.findByText(/Version 3 compared with the previous one/)).toBeInTheDocument()
    expect(screen.getByText(/new valid_ins · 2 fields moved/)).toBeInTheDocument()
  })

  it('refuses to save a recipe that is not JSON', async () => {
    renderPanel()
    await userEvent.click(await screen.findByRole('button', { name: 'Recipe' }))
    const editor = screen.getByLabelText('Project JSON')
    await userEvent.clear(editor)
    await userEvent.type(editor, 'not json')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Not valid JSON')
    expect(saveFormat).not.toHaveBeenCalled()
  })
})
