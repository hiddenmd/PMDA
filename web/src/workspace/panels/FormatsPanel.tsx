import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { errorMessage } from '../../api/auth'
import { addDemoFormat, createFormat, deleteFormat, formatKeys, getFormat } from '../../api/formats'
import { useAuth } from '../../auth/authContext'
import { Dialog } from '../../components/Dialog'
import { AlertCircle, FileCode, Plus, Sparkles, Trash2 } from '../../components/icons'
import { FormatDetailView, ReadyChip } from '../../features/formats/FormatDetailView'
import { useFormats } from '../../hooks/useFormats'

/**
 * Recording formats defined by vendor headers (admins): one project per recorder, each with its .h versions, the
 * recipe that merges raw events, and the rules that feed the map, RWR and timeline.
 */
export function FormatsPanel() {
  const { isAdmin } = useAuth()
  const queryClient = useQueryClient()
  const formats = useFormats()
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [removing, setRemoving] = useState<string | null>(null)
  const current = selected ?? formats.data?.[0]?.id ?? null
  const detail = useQuery({
    queryKey: formatKeys.detail(current ?? ''),
    queryFn: ({ signal }) => getFormat(current!, signal),
    enabled: isAdmin && current !== null,
  })

  const changed = (id?: string) => {
    void queryClient.invalidateQueries({ queryKey: formatKeys.all })
    if (id) setSelected(id)
  }
  const demo = useMutation({ mutationFn: addDemoFormat, onSuccess: (d) => changed(d.project.id) })
  const remove = useMutation({
    mutationFn: (id: string) => deleteFormat(id),
    onSuccess: () => {
      setRemoving(null)
      setSelected(null)
      changed()
    },
  })

  if (!isAdmin) return <div className="empty">Only administrators manage recording formats.</div>

  return (
    <div className="panel-body">
      <div className="section-title">
        <h3>Recording formats {formats.data && <span className="muted mono">({formats.data.length})</span>}</h3>
        <span style={{ display: 'flex', gap: 4 }}>
          <button type="button" className="btn btn--secondary btn--compact" disabled={demo.isPending} onClick={() => demo.mutate()}>
            <Sparkles size={14} aria-hidden /> Add demo project
          </button>
          <button type="button" className="btn btn--primary btn--compact" onClick={() => setCreating(true)}>
            <Plus size={14} aria-hidden /> New project
          </button>
        </span>
      </div>
      <p className="muted small">
        Each project reads one recorder&apos;s raw dumps with its vendor <span className="mono">.h</span>: upload the header, check the report and a
        dry run, then import. A new header version adds columns to the same tables, so existing SQL reports keep working.
      </p>
      {(formats.isError || demo.isError || remove.isError) && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(formats.error ?? demo.error ?? remove.error)}</span>
        </p>
      )}
      {formats.data?.length === 0 && (
        <div className="empty">
          <FileCode size={20} aria-hidden />
          <p>No projects yet. Create one per recorder and upload its vendor header, or add the demo project to see how it works.</p>
        </div>
      )}
      {formats.data && formats.data.length > 0 && (
        <div className="table-wrap">
          <table className="table table--compact">
            <thead>
              <tr>
                <th scope="col">Project</th>
                <th scope="col">Header</th>
                <th scope="col">State</th>
                <th scope="col">Tables</th>
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {formats.data.map((f) => (
                <tr key={f.id} aria-selected={f.id === current} className={f.id === current ? 'is-selected' : undefined}>
                  <td>
                    <button type="button" className="btn btn--ghost btn--compact format-name" onClick={() => setSelected(f.id)}>
                      {f.name} <span className="muted mono">{f.id}</span>
                    </button>
                  </td>
                  <td className="mono">{f.version === null ? '—' : `v${f.version}`}</td>
                  <td>
                    <ReadyChip canImport={f.canImport} errors={f.errors} hasHeader={f.version !== null} />
                    {f.warnings > 0 && <span className="muted small"> {f.warnings} warning{f.warnings === 1 ? '' : 's'}</span>}
                  </td>
                  <td className="mono">{f.messages}</td>
                  <td style={{ textAlign: 'right' }}>
                    <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Remove ${f.name}`} title="Remove project" onClick={() => setRemoving(f.id)}>
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {detail.isError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(detail.error)}</span>
        </p>
      )}
      {detail.data && <FormatDetailView key={detail.data.project.id} detail={detail.data} />}

      <Dialog open={creating} title="New recording project" icon={<FileCode size={18} aria-hidden />} onClose={() => setCreating(false)}>
        {creating && (
          <CreateForm
            onCancel={() => setCreating(false)}
            onCreated={(id) => {
              setCreating(false)
              changed(id)
            }}
          />
        )}
      </Dialog>
      <Dialog open={removing !== null} title={`Remove ${removing ?? ''}?`} icon={<Trash2 size={18} aria-hidden />} onClose={() => setRemoving(null)}>
        <p className="small">
          The project, its header versions and recipe are deleted from this server. Tables already created in the databases, and the flights in
          them, stay.
        </p>
        <div className="dialog__actions">
          <button type="button" className="btn btn--secondary" onClick={() => setRemoving(null)}>
            Cancel
          </button>
          <button type="button" className="btn btn--danger" disabled={remove.isPending} onClick={() => removing && remove.mutate(removing)}>
            Remove project
          </button>
        </div>
      </Dialog>
    </div>
  )
}

function CreateForm({ onCancel, onCreated }: { onCancel: () => void; onCreated: (id: string) => void }) {
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const create = useMutation({ mutationFn: () => createFormat(id.trim(), name.trim()), onSuccess: (d) => onCreated(d.project.id) })
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault()
        create.mutate()
      }}
    >
      <label className="field">
        <span>Id</span>
        <input className="input mono" value={id} onChange={(e) => setId(e.target.value)} placeholder="PROJ-A" maxLength={32} required autoFocus />
        <span className="muted small">Letters, digits, - and _. It becomes the flights&apos; customer id and the SQL schema.</span>
      </label>
      <label className="field">
        <span>Name</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Project A recorder" maxLength={100} />
      </label>
      {create.isError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(create.error)}</span>
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--secondary" onClick={onCancel}>
          Cancel
        </button>
        <button type="submit" className="btn btn--primary" disabled={create.isPending || id.trim() === ''}>
          Create
        </button>
      </div>
    </form>
  )
}
