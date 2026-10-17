import { AlertCircle, CheckCircle2, Copy, Loader2, RotateCcw, UploadCloud, XCircle } from 'lucide-react'
import { useState, type DragEvent, type ReactNode } from 'react'
import { ProgressBar } from '../../components/ProgressBar'
import { useImport, type ImportViewModel } from '../../hooks/useImport'
import { formatBytes, formatNumber } from '../../lib/format'

interface ImportPanelProps {
  onOpenFlight: (flightId: string) => void
}

export function ImportPanel({ onOpenFlight }: ImportPanelProps) {
  const vm = useImport()
  return <ImportPanelView {...vm} onOpenFlight={onOpenFlight} />
}

/** Renders an import view model; no state of its own beyond drag highlighting. */
export function ImportPanelView(vm: ImportViewModel & ImportPanelProps) {
  const { status, file, job, error } = vm
  const flightId = job?.flightId
  const canStart = status === 'idle' || status === 'completed' || status === 'duplicate' || status === 'failed' || status === 'cancelled'

  return (
    <section className="import" aria-label="Import flight">

      {canStart && <FilePicker onFile={vm.start} />}

      {status === 'uploading' && (
        <Active title={`Uploading ${file?.name ?? ''}`} onCancel={vm.cancel}>
          <ProgressBar value={vm.uploadPercent} label="Upload" />
        </Active>
      )}

      {status === 'queued' && <Active title={`${job?.fileName} is waiting for a worker`} onCancel={vm.cancel} />}

      {status === 'processing' && job && (
        <Active title={`Importing ${job.fileName}`} onCancel={vm.cancel}>
          <ProgressBar value={vm.processPercent} label="Parsing" />
          <p className="muted mono" style={{ fontSize: 12 }}>
            {formatNumber(job.eventCount)} events · {formatNumber(job.errorCount)} problems · {formatBytes(job.bytesRead)} of{' '}
            {formatBytes(job.totalBytes)}
          </p>
        </Active>
      )}

      {status === 'cancelling' && (
        <Notice kind="info" icon={<Loader2 size={16} aria-hidden />}>
          Cancelling…
        </Notice>
      )}

      {status === 'completed' && job && (
        <Notice kind="ok" icon={<CheckCircle2 size={16} aria-hidden />}>
          <p>
            <strong>{job.fileName}</strong> imported: {formatNumber(job.eventCount)} events, {formatNumber(job.errorCount)} problems.
          </p>
          {flightId && (
            <button type="button" className="btn btn--primary btn--compact" onClick={() => vm.onOpenFlight(flightId)}>
              Open flight
            </button>
          )}
        </Notice>
      )}

      {status === 'duplicate' && (
        <Notice kind="warn" icon={<Copy size={16} aria-hidden />}>
          {job?.fileName} was already imported.
        </Notice>
      )}

      {status === 'cancelled' && (
        <Notice kind="info" icon={<XCircle size={16} aria-hidden />}>
          Import cancelled.
        </Notice>
      )}

      {status === 'failed' && (
        <Notice kind="error" icon={<AlertCircle size={16} aria-hidden />} alert>
          <p>{error ?? 'The import failed.'}</p>
          {file && (
            <button type="button" className="btn btn--secondary btn--compact" onClick={vm.retry}>
              <RotateCcw size={14} aria-hidden />
              Retry {file.name}
            </button>
          )}
        </Notice>
      )}
    </section>
  )
}

function Notice({ kind, icon, alert, children }: { kind: 'ok' | 'warn' | 'error' | 'info'; icon: ReactNode; alert?: boolean; children: ReactNode }) {
  return (
    <div className={`notice notice--${kind}`} role={alert ? 'alert' : undefined}>
      {icon}
      <div className="notice__body">{children}</div>
    </div>
  )
}

function Active({ title, onCancel, children }: { title: string; onCancel: () => void; children?: ReactNode }) {
  return (
    <div className="import-active">
      <div className="import-active__row">
        <span className="import-active__name" title={title}>
          {title}
        </span>
        <button type="button" className="btn btn--secondary btn--compact" onClick={onCancel}>
          Cancel
        </button>
      </div>
      {children}
    </div>
  )
}

function FilePicker({ onFile }: { onFile: (file: File) => void }) {
  const [dragging, setDragging] = useState(false)

  const onDrop = (e: DragEvent) => {
    e.preventDefault()
    setDragging(false)
    const file = e.dataTransfer.files[0]
    if (file) onFile(file)
  }

  return (
    <label
      className={`dropzone${dragging ? ' dropzone--active' : ''}`}
      onDragOver={(e) => {
        e.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
    >
      <UploadCloud size={20} aria-hidden />
      <span className="dropzone__text">
        Drop a recording or <strong>browse</strong>
      </span>
      <input
        type="file"
        className="visually-hidden"
        aria-label="Flight recording"
        onChange={(e) => {
          const file = e.target.files?.[0]
          if (file) onFile(file)
          e.target.value = '' // allow choosing the same file again
        }}
      />
    </label>
  )
}
