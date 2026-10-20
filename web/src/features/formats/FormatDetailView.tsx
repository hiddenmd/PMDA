import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Fragment, useRef, useState } from 'react'
import { errorMessage } from '../../api/auth'
import { dryRun, formatKeys, saveFormat, uploadHeader, type DryRunReport, type FormatDetail, type FormatDiff, type FormatMessage, type FormatProject } from '../../api/formats'
import { AlertCircle, AlertTriangle, CheckCircle2, ChevronRight, Info, Save, UploadCloud } from '../../components/icons'
import { formatBytes, formatNumber } from '../../lib/format'

type Tab = 'header' | 'messages' | 'recipe' | 'dry-run'

const TABS: { id: Tab; label: string }[] = [
  { id: 'header', label: 'Header' },
  { id: 'messages', label: 'Messages' },
  { id: 'recipe', label: 'Recipe' },
  { id: 'dry-run', label: 'Dry run' },
]

/** One project: its header versions and problems, the messages and tables it defines, its recipe, and a dry run. */
export function FormatDetailView({ detail }: { detail: FormatDetail }) {
  const [tab, setTab] = useState<Tab>(detail.version === null ? 'header' : 'messages')
  const errors = detail.diagnostics.filter((d) => d.severity === 'Error').length
  return (
    <div className="format-detail">
      <div className="format-detail__head">
        <div>
          <h4>
            {detail.project.name} <span className="muted mono">{detail.project.id}</span>
          </h4>
          <p className="muted small">
            {detail.version === null ? 'No header yet' : `Header version ${detail.version}`} · SQL schema <span className="mono">{detail.sqlSchema}</span>
            {detail.frame && (
              <>
                {' '}
                · record header {detail.frame.headerBytes} bytes, sync <span className="mono">{detail.frame.sync || 'none'}</span>
              </>
            )}
          </p>
        </div>
        <ReadyChip canImport={detail.canImport} errors={errors} hasHeader={detail.version !== null} />
      </div>
      <div className="segmented" role="group" aria-label="Project sections">
        {TABS.map((t) => (
          <button key={t.id} type="button" aria-pressed={tab === t.id} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'header' && <HeaderTab detail={detail} />}
      {tab === 'messages' && <MessagesTab messages={detail.messages} />}
      {tab === 'recipe' && <RecipeTab key={JSON.stringify(detail.project)} detail={detail} />}
      {tab === 'dry-run' && <DryRunTab detail={detail} />}
    </div>
  )
}

export function ReadyChip({ canImport, errors, hasHeader }: { canImport: boolean; errors: number; hasHeader: boolean }) {
  if (!hasHeader) return <span className="chip chip--deleting">No header</span>
  if (canImport) return <span className="chip chip--completed">Ready</span>
  return <span className="chip chip--failed">{errors === 1 ? '1 error' : `${errors} errors`}</span>
}

function HeaderTab({ detail }: { detail: FormatDetail }) {
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const upload = useMutation({
    mutationFn: (file: File) => uploadHeader(detail.project.id, file),
    onSuccess: (result) => {
      queryClient.setQueryData(formatKeys.detail(detail.project.id), result.detail)
      void queryClient.invalidateQueries({ queryKey: formatKeys.list })
    },
  })
  const severities = ['Error', 'Warning', 'Info'] as const

  return (
    <div className="format-section">
      <div className="format-upload">
        <input
          ref={input}
          type="file"
          accept=".h,.hpp,.txt,text/plain"
          hidden
          aria-label="Header file"
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) upload.mutate(file)
            e.target.value = ''
          }}
        />
        <button type="button" className="btn btn--primary btn--compact" disabled={upload.isPending} onClick={() => input.current?.click()}>
          <UploadCloud size={14} aria-hidden /> {detail.version === null ? 'Upload vendor .h' : 'Upload new version'}
        </button>
        <span className="muted small">Each upload is kept as a version. Nothing changes in the databases until a flight is imported with it.</span>
      </div>
      {upload.isError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(upload.error)}</span>
        </p>
      )}
      {upload.data && !upload.data.newVersion && <p className="notice">That file is the same as version {upload.data.detail.version}; nothing changed.</p>}
      {upload.data?.diff && <DiffView diff={upload.data.diff} version={upload.data.detail.version ?? 0} />}

      <h5>Report</h5>
      {detail.diagnostics.length === 0 ? (
        <p className="small">
          <CheckCircle2 size={14} aria-hidden /> The header reads cleanly: {detail.messages.length} messages, {detail.enums} enums.
        </p>
      ) : (
        <ul className="format-diagnostics">
          {severities.flatMap((sev) =>
            detail.diagnostics
              .filter((d) => d.severity === sev)
              .map((d, i) => (
                <li key={`${sev}-${i}`} className={`format-diagnostic format-diagnostic--${sev.toLowerCase()}`}>
                  {sev === 'Error' ? <AlertCircle size={14} aria-hidden /> : sev === 'Warning' ? <AlertTriangle size={14} aria-hidden /> : <Info size={14} aria-hidden />}
                  <span>
                    {d.line > 0 && <span className="mono muted">line {d.line} · </span>}
                    {d.message}
                  </span>
                </li>
              )),
          )}
        </ul>
      )}

      {detail.versions.length > 0 && (
        <>
          <h5>Versions</h5>
          <table className="table table--compact">
            <thead>
              <tr>
                <th scope="col">Version</th>
                <th scope="col">Saved</th>
                <th scope="col">Size</th>
                <th scope="col">SHA-256</th>
              </tr>
            </thead>
            <tbody>
              {[...detail.versions].reverse().map((v) => (
                <tr key={v.version}>
                  <td className="mono">
                    {v.version}
                    {v.version === detail.version && <span className="chip chip--tag">current</span>}
                  </td>
                  <td>{new Date(v.savedUtc).toLocaleString()}</td>
                  <td className="mono">{formatBytes(v.bytes)}</td>
                  <td className="mono muted">{v.sha256.slice(0, 16)}…</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  )
}

function DiffView({ diff, version }: { diff: FormatDiff; version: number }) {
  if (diff.addedMessages.length === 0 && diff.removedMessages.length === 0 && diff.changedStructs.length === 0)
    return <p className="notice">Version {version} lays out every message exactly like the previous one.</p>
  return (
    <div className="notice format-diff" role="status">
      <strong>Version {version} compared with the previous one</strong>
      <ul>
        {diff.addedMessages.map((m) => (
          <li key={`a-${m}`}>New message {m}</li>
        ))}
        {diff.removedMessages.map((m) => (
          <li key={`r-${m}`}>Message {m} is gone (its table and rows stay)</li>
        ))}
        {diff.changedStructs.map((c) => (
          <li key={c.struct}>
            <span className="mono">{c.struct}</span>: {c.sizeBefore} → {c.sizeAfter} bytes
            {c.added.length > 0 && <> · new {c.added.join(', ')}</>}
            {c.removed.length > 0 && <> · removed {c.removed.join(', ')}</>}
            {c.moved.length > 0 && <> · {c.moved.length} field{c.moved.length === 1 ? '' : 's'} moved</>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function MessagesTab({ messages }: { messages: FormatMessage[] }) {
  const [open, setOpen] = useState<string | null>(null)
  if (messages.length === 0) return <p className="muted small">Upload a header to see its messages.</p>
  return (
    <div className="table-wrap format-section">
      <table className="table table--compact">
        <thead>
          <tr>
            <th scope="col">Event</th>
            <th scope="col">Struct</th>
            <th scope="col">Table</th>
            <th scope="col">Title</th>
            <th scope="col">Columns</th>
            <th scope="col">Bytes</th>
          </tr>
        </thead>
        <tbody>
          {messages.map((m) => {
            const expanded = open === m.key
            return (
              <Fragment key={m.key}>
                <tr className={m.stored ? undefined : 'muted'}>
                  <td className="mono">{m.events.length > 0 ? m.events.join(', ') : '—'}</td>
                  <td>
                    <button type="button" className="btn btn--ghost btn--compact format-expand" aria-expanded={expanded} onClick={() => setOpen(expanded ? null : m.key)}>
                      <ChevronRight size={12} aria-hidden className={expanded ? 'rotate-90' : undefined} />
                      <span className="mono">{m.key}</span>
                    </button>
                  </td>
                  <td className="mono">
                    {m.stored ? (
                      m.table
                    ) : m.tracked ? (
                      <span title="Its events feed a track, which writes the merged rows">merged</span>
                    ) : (
                      <span title="Every field is <Parse>False: records are kept raw only">raw only</span>
                    )}
                  </td>
                  <td>
                    {m.title ?? ''}
                    {m.hidden && <span className="chip chip--tag">hidden</span>}
                  </td>
                  <td className="mono">
                    {m.columns.length}
                    {m.children.length > 0 && <span className="muted"> + {m.children.length} list{m.children.length === 1 ? '' : 's'}</span>}
                  </td>
                  <td className="mono">{m.sizeBytes || ''}</td>
                </tr>
                {expanded && (
                  <tr>
                    <td colSpan={6}>
                      <ColumnList message={m} />
                    </td>
                  </tr>
                )}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function ColumnList({ message }: { message: FormatMessage }) {
  return (
    <div className="format-columns">
      <table className="table table--compact">
        <thead>
          <tr>
            <th scope="col">Field</th>
            <th scope="col">Column</th>
            <th scope="col">Type</th>
            <th scope="col">Unit</th>
            <th scope="col">Enum</th>
          </tr>
        </thead>
        <tbody>
          {message.columns.map((c) => (
            <tr key={c.field}>
              <td className="mono">{c.field}</td>
              <td className="mono">{c.name !== c.field ? c.name : ''}</td>
              <td className="mono muted">{c.type}</td>
              <td>{c.unit ?? ''}</td>
              <td className="mono muted">{c.enum ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {message.children.map((c) => (
        <p key={c.field} className="small muted">
          List <span className="mono">{c.field}</span> → table <span className="mono">{c.table}</span>, up to {c.maxCount} rows counted by{' '}
          <span className="mono">{c.countField}</span>.
        </p>
      ))}
    </div>
  )
}

function RecipeTab({ detail }: { detail: FormatDetail }) {
  const queryClient = useQueryClient()
  const [text, setText] = useState(() => JSON.stringify(detail.project, null, 2))
  const [parseError, setParseError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: (project: FormatProject) => saveFormat(project),
    onSuccess: (saved) => {
      queryClient.setQueryData(formatKeys.detail(detail.project.id), saved)
      void queryClient.invalidateQueries({ queryKey: formatKeys.list })
    },
  })
  const dirty = text !== JSON.stringify(detail.project, null, 2)

  return (
    <div className="format-section">
      <p className="muted small">
        Settings, recipe and event rules as JSON. The <span className="mono">recipe</span> binds event ids to structs, follows tracked objects
        through new/update/extension/delete events, joins lists and attaches the aircraft state; <span className="mono">events</span> map rows to
        the map, RWR and timeline. Event fields: {detail.eventProperties.join(', ')}.
      </p>
      <textarea
        className="input format-json"
        spellCheck={false}
        aria-label="Project JSON"
        value={text}
        onChange={(e) => {
          setText(e.target.value)
          setParseError(null)
        }}
      />
      {(parseError || save.isError) && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{parseError ?? errorMessage(save.error)}</span>
        </p>
      )}
      {save.isSuccess && !dirty && <p className="notice">Saved. {save.data.canImport ? 'The project is ready to import.' : 'The report shows what is still wrong.'}</p>}
      <div className="dialog__actions">
        <button type="button" className="btn btn--secondary btn--compact" disabled={!dirty} onClick={() => setText(JSON.stringify(detail.project, null, 2))}>
          Discard changes
        </button>
        <button
          type="button"
          className="btn btn--primary btn--compact"
          disabled={!dirty || save.isPending}
          onClick={() => {
            let project: FormatProject
            try {
              project = JSON.parse(text) as FormatProject
            } catch (e) {
              setParseError(`Not valid JSON: ${(e as Error).message}`)
              return
            }
            save.mutate({ ...project, id: detail.project.id })
          }}
        >
          <Save size={14} aria-hidden /> Save
        </button>
      </div>
    </div>
  )
}

function DryRunTab({ detail }: { detail: FormatDetail }) {
  const input = useRef<HTMLInputElement>(null)
  const run = useMutation({ mutationFn: (file: File) => dryRun(detail.project.id, file) })
  return (
    <div className="format-section">
      <div className="format-upload">
        <input
          ref={input}
          type="file"
          hidden
          aria-label="Recording file"
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) run.mutate(file)
            e.target.value = ''
          }}
        />
        <button type="button" className="btn btn--primary btn--compact" disabled={!detail.canImport || run.isPending} onClick={() => input.current?.click()}>
          <UploadCloud size={14} aria-hidden /> {run.isPending ? 'Decoding…' : 'Try a recording'}
        </button>
        <span className="muted small">Decodes the file with this header and recipe and reports what an import would store. Nothing is saved.</span>
      </div>
      {run.isError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(run.error)}</span>
        </p>
      )}
      {run.data && <DryRunResult report={run.data} />}
    </div>
  )
}

function DryRunResult({ report }: { report: DryRunReport }) {
  const unknown = Object.entries(report.unknownEvents)
  const short = Object.entries(report.shortByEvent)
  const orphans = report.updatesWithoutNew + report.extensionsWithoutNew + report.deletesWithoutNew
  return (
    <div className="format-dry-run">
      <dl className="format-facts">
        <dt>Records</dt>
        <dd className="mono">{formatNumber(report.records)}</dd>
        <dt>Flight</dt>
        <dd>
          {report.dateFound ? new Date(report.flightDate).toLocaleDateString() : <span className="muted">no date found</span>}
          {report.tail && <span className="mono"> · {report.tail}</span>}
        </dd>
        <dt>Time span</dt>
        <dd className="mono">
          {report.firstUtc && report.lastUtc ? `${report.firstUtc.slice(11, 19)} – ${report.lastUtc.slice(11, 19)} UTC` : '—'}
        </dd>
        <dt>Tracks</dt>
        <dd className="mono">
          {formatNumber(report.tracksStarted)} started{orphans > 0 && <span className="muted"> · {formatNumber(orphans)} events for unannounced tracks</span>}
        </dd>
        <dt>Common events</dt>
        <dd className="mono">{formatNumber(report.events)}</dd>
      </dl>
      <h5>Rows per table</h5>
      <table className="table table--compact">
        <tbody>
          {Object.entries(report.rowsByTable).map(([table, rows]) => (
            <tr key={table}>
              <td className="mono">{table}</td>
              <td className="mono">{formatNumber(rows)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {unknown.length > 0 && (
        <p className="notice">
          <AlertTriangle size={14} aria-hidden /> Events the header does not describe (kept raw only):{' '}
          <span className="mono">{unknown.map(([id, n]) => `${id} ×${formatNumber(n)}`).join(', ')}</span>
        </p>
      )}
      {short.length > 0 && (
        <p className="notice">
          <AlertTriangle size={14} aria-hidden /> Records shorter than their struct: <span className="mono">{short.map(([id, n]) => `${id} ×${formatNumber(n)}`).join(', ')}</span>
        </p>
      )}
      {report.diagnostics.length > 0 && (
        <>
          <h5>
            Problems <span className="muted mono">({formatNumber(report.diagnosticCount)})</span>
          </h5>
          <ul className="format-diagnostics">
            {report.diagnostics.map((d, i) => (
              <li key={i} className="format-diagnostic format-diagnostic--warning">
                <span className="mono muted">@{d.fileOffset}</span> {d.message}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}
