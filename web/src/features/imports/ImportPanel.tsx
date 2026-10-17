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
    <section className="panel" aria-labelledby="import-title">
      <h2 id="import-title">Import flight</h2>

      {canStart && <FilePicker onFile={vm.start} />}

      {status === 'uploading' && (
        <Active title={`Uploading ${file?.name ?? ''}`} onCancel={vm.cancel}>
          <ProgressBar value={vm.uploadPercent} label="Upload" />
        </Active>
      )}

      {status === 'queued' && (
        <Active title={`${job?.fileName} is waiting for a worker`} onCancel={vm.cancel} />
      )}

      {status === 'processing' && job && (
        <Active title={`Importing ${job.fileName}`} onCancel={vm.cancel}>
          <ProgressBar value={vm.processPercent} label="Parsing" />
          <p className="muted">
            {formatNumber(job.eventCount)} events · {formatNumber(job.errorCount)} problems · {formatBytes(job.bytesRead)} of{' '}
            {formatBytes(job.totalBytes)}
          </p>
        </Active>
      )}

      {status === 'cancelling' && <p className="notice">Cancelling…</p>}

      {status === 'completed' && job && (
        <div className="notice notice--ok">
          <p>
            <strong>{job.fileName}</strong> imported: {formatNumber(job.eventCount)} events, {formatNumber(job.errorCount)} problems.
          </p>
          {flightId && (
            <button type="button" onClick={() => vm.onOpenFlight(flightId)}>
              Open flight
            </button>
          )}
        </div>
      )}

      {status === 'duplicate' && <p className="notice notice--warn">{job?.fileName} was already imported.</p>}

      {status === 'cancelled' && <p className="notice">Import cancelled.</p>}

      {status === 'failed' && (
        <div className="notice notice--error" role="alert">
          <p>{error ?? 'The import failed.'}</p>
          {file && (
            <button type="button" onClick={vm.retry}>
              Retry {file.name}
            </button>
          )}
        </div>
      )}
    </section>
  )
}

function Active({ title, onCancel, children }: { title: string; onCancel: () => void; children?: ReactNode }) {
  return (
    <div className="active">
      <p className="active__title">{title}</p>
      {children}
      <button type="button" className="button--secondary" onClick={onCancel}>
        Cancel
      </button>
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
      <span>Drop a flight recording here, or choose a file</span>
      <input
        type="file"
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
