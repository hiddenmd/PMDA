import { useSelector } from '@xstate/react'
import { AlertCircle, CheckCircle2, Clock, Copy, FolderOpen, Loader2, RotateCcw, UploadCloud, X, XCircle } from '../../components/icons'
import { useEffect, useRef, useState, type DragEvent } from 'react'
import type { DatabaseChoice } from '../../api/databases'
import { useDatabaseChoices } from '../../hooks/useDatabases'
import { useImportBatch, type ImportBatch } from '../../hooks/useImportBatch'
import { formatBytes, formatCompact, formatNumber } from '../../lib/format'
import { isFinished, type BatchItem, type BatchSummary, type FileImportRef, type FileStatus } from '../../machines/importBatchMachine'
import { importProgress } from '../../machines/importMachine'
import { droppedFiles, pickedFiles } from './droppedFiles'

interface ImportPanelProps {
  onOpenFlight: (flightId: string) => void
}

export function ImportPanel({ onOpenFlight }: ImportPanelProps) {
  const batch = useImportBatch()
  const choices = useDatabaseChoices()
  return (
    <ImportPanelView
      batch={batch}
      onOpenFlight={onOpenFlight}
      targets={choices.data?.databases.filter((d) => !d.readOnly)}
      defaultTarget={choices.data?.defaultImportId ?? null}
    />
  )
}

interface ImportPanelViewProps extends ImportPanelProps {
  batch: ImportBatch
  /** Databases that take imports; the picker shows when there is a choice. */
  targets?: readonly DatabaseChoice[]
  defaultTarget?: string | null
}

/** Drop zone plus one row per file of the batch. */
export function ImportPanelView({ batch, onOpenFlight, targets = [], defaultTarget = null }: ImportPanelViewProps) {
  const { items, summary } = batch
  const [picked, setPicked] = useState<string | null>(null)
  // The admin may change the default while this panel is open: follow it until the user picks one.
  const target = picked !== null && targets.some((t) => t.id === picked) ? picked : (defaultTarget ?? targets[0]?.id ?? null)
  const children = useSelector(batch.actor, (s) => s.children as Record<string, FileImportRef | undefined>)
  const active = summary.waiting + summary.uploading + summary.importing
  const finished = summary.total - active

  return (
    <section className="import" aria-label="Import flights">
      {targets.length > 1 && (
        <label className="field import-target">
          <span>Import into</span>
          <select className="select select--compact" value={target ?? ''} onChange={(e) => setPicked(e.target.value)}>
            {targets.map((t) => (
              <option key={t.id} value={t.id}>
                {t.id === defaultTarget ? `${t.name} (default)` : t.name}
              </option>
            ))}
          </select>
        </label>
      )}
      <FilePicker onFiles={(files) => batch.add(files, target)} compact={items.length > 0} />

      {items.length > 0 && (
        <div className="import-batch">
          <div className="import-batch__head">
            <span className="import-batch__summary" role="status">
              {describe(summary)}
            </span>
            {active > 0 && (
              <button type="button" className="btn btn--ghost btn--compact" onClick={batch.cancelAll}>
                Cancel all
              </button>
            )}
            {finished > 0 && (
              <button type="button" className="btn btn--ghost btn--compact" onClick={batch.clearFinished}>
                Clear finished
              </button>
            )}
          </div>
          <ul className="import-list" aria-label="Files">
            {items.map((item) => {
              const child = children[item.id]
              return child ? <ImportRow key={item.id} item={item} child={child} batch={batch} onOpenFlight={onOpenFlight} /> : null
            })}
          </ul>
        </div>
      )}
    </section>
  )
}

function describe(s: BatchSummary): string {
  const parts = [
    s.completed > 0 && `${s.completed} imported`,
    s.importing > 0 && `${s.importing} importing`,
    s.uploading > 0 && `${s.uploading} uploading`,
    s.waiting > 0 && `${s.waiting} waiting`,
    s.duplicate > 0 && `${s.duplicate} already imported`,
    s.failed > 0 && `${s.failed} failed`,
    s.cancelled > 0 && `${s.cancelled} cancelled`,
  ].filter(Boolean)
  return `${s.total} ${s.total === 1 ? 'file' : 'files'}: ${parts.join(' · ')}`
}

const ICONS: Record<FileStatus, typeof Clock> = {
  waiting: Clock,
  starting: Clock,
  uploading: Loader2,
  queued: Clock,
  processing: Loader2,
  cancelling: Loader2,
  completed: CheckCircle2,
  duplicate: Copy,
  failed: AlertCircle,
  cancelled: XCircle,
}

function ImportRow({ item, child, batch, onOpenFlight }: { item: BatchItem; child: FileImportRef; batch: ImportBatch } & ImportPanelProps) {
  // Progress ticks re-render only this row; the status comes from the batch.
  const context = useSelector(child, (s) => s.context)
  const { status } = item
  const { job, error } = context
  const { uploadPercent, processPercent } = importProgress(context)
  const Icon = ICONS[status]
  const busy = status === 'uploading' || status === 'processing' || status === 'cancelling'

  let text: string
  let percent: number | null = null
  switch (status) {
    case 'waiting':
    case 'starting':
      text = 'Waiting to upload'
      break
    case 'uploading':
      text = `Uploading ${uploadPercent}%`
      percent = uploadPercent
      break
    case 'queued':
      text = 'Queued on the server'
      break
    case 'processing':
      text = `Importing ${processPercent}% · ${formatCompact(job?.eventCount ?? 0)} events`
      percent = processPercent
      break
    case 'cancelling':
      text = 'Cancelling…'
      break
    case 'completed':
      text = `Imported · ${formatNumber(job?.eventCount ?? 0)} events${job?.errorCount ? ` · ${formatNumber(job.errorCount)} problems` : ''}`
      break
    case 'duplicate':
      text = 'Already imported'
      break
    case 'failed':
      text = error ?? 'The import failed.'
      break
    case 'cancelled':
      text = 'Cancelled'
      break
  }

  return (
    <li className={`import-row import-row--${status}`}>
      <Icon size={14} aria-hidden className={busy ? 'spin' : undefined} />
      <div className="import-row__main">
        <div className="import-row__line">
          <span className="import-row__name" title={item.file.webkitRelativePath || item.file.name}>
            {item.file.name}
          </span>
          <span className="import-row__size">{formatBytes(item.file.size)}</span>
        </div>
        <div className="import-row__status" title={text}>
          {text}
        </div>
        {percent !== null && (
          <div className="import-row__track" role="progressbar" aria-label={`${item.file.name} progress`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent}>
            <div className="progress__fill" style={{ width: `${percent}%` }} />
          </div>
        )}
      </div>
      <div className="import-row__actions">
        {status === 'completed' && job?.flightId && (
          <button type="button" className="btn btn--secondary btn--compact" onClick={() => onOpenFlight(job.flightId!)}>
            Open
          </button>
        )}
        {status === 'failed' && (
          <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Retry ${item.file.name}`} title="Retry" onClick={() => batch.retry(item.id)}>
            <RotateCcw size={14} aria-hidden />
          </button>
        )}
        {isFinished(status) ? (
          <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Remove ${item.file.name} from the list`} title="Remove from list" onClick={() => batch.remove(item.id)}>
            <X size={14} aria-hidden />
          </button>
        ) : (
          status !== 'cancelling' && (
            <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Cancel ${item.file.name}`} title="Cancel" onClick={() => batch.cancel(item.id)}>
              <XCircle size={14} aria-hidden />
            </button>
          )
        )}
      </div>
    </li>
  )
}

function FilePicker({ onFiles, compact }: { onFiles: (files: File[]) => void; compact: boolean }) {
  const [dragging, setDragging] = useState(false)
  const folderRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    // Not in React's input props: lets the picker choose a folder and import every file in it.
    folderRef.current?.setAttribute('webkitdirectory', '')
  }, [])

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setDragging(false)
    void droppedFiles(e.dataTransfer).then(onFiles)
  }

  return (
    <div className="import-pick">
      <label
        className={`dropzone${dragging ? ' dropzone--active' : ''}${compact ? ' dropzone--compact' : ''}`}
        onDragOver={(e) => {
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <UploadCloud size={20} aria-hidden />
        <span className="dropzone__text">
          Drop recordings or folders, or <strong>browse</strong>
        </span>
        <input
          type="file"
          multiple
          className="visually-hidden"
          aria-label="Flight recordings"
          onChange={(e) => {
            onFiles(pickedFiles(e.target.files))
            e.target.value = '' // allow choosing the same files again
          }}
        />
      </label>
      <label className="btn btn--ghost btn--icon" title="Import a whole folder">
        <FolderOpen size={16} aria-hidden />
        <input
          ref={folderRef}
          type="file"
          className="visually-hidden"
          aria-label="Folder of flight recordings"
          onChange={(e) => {
            onFiles(pickedFiles(e.target.files))
            e.target.value = ''
          }}
        />
      </label>
    </div>
  )
}
