import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { errorMessage } from '../../api/auth'
import { databaseKeys, listDatabases, removeDatabase, setDefaultDatabase, updateDatabase, type DatabaseInfo, type DatabaseList } from '../../api/databases'
import { flightKeys } from '../../hooks/useFlights'
import { useAuth } from '../../auth/authContext'
import { Dialog } from '../../components/Dialog'
import { AlertCircle, Database, Pencil, Plus, Search, Trash2 } from '../../components/icons'
import { DatabaseForm } from '../../features/database/DatabaseForm'
import { FindDatabases } from '../../features/database/FindDatabases'
import { formatNumber } from '../../lib/format'

type Editing = { mode: 'add' } | { mode: 'find' } | { mode: 'edit'; database: DatabaseInfo } | { mode: 'remove'; database: DatabaseInfo } | null

const STATE_CHIP = { Ready: 'chip--completed', Unreachable: 'chip--failed', Disabled: 'chip--deleting' } as const

/** The flight databases PMDA works with (admins). The API enforces the role; this panel only explains it to others. */
export function DatabasesPanel() {
  const { isAdmin } = useAuth()
  const queryClient = useQueryClient()
  const databases = useQuery({ queryKey: databaseKeys.list, queryFn: ({ signal }) => listDatabases(signal), enabled: isAdmin })
  const [editing, setEditing] = useState<Editing>(null)

  // Every change answers with the fresh list; flights and the pickers depend on it too.
  const applied = (list: DatabaseList) => {
    queryClient.setQueryData(databaseKeys.list, list)
    void queryClient.invalidateQueries({ queryKey: databaseKeys.choices })
    void queryClient.invalidateQueries({ queryKey: flightKeys.all })
  }
  const action = useMutation({
    mutationFn: (run: () => Promise<DatabaseList>) => run(),
    onSuccess: applied,
  })

  if (!isAdmin) return <div className="empty">Only administrators manage databases.</div>

  return (
    <div className="panel-body">
      <div className="section-title">
        <h3>Databases {databases.data && <span className="muted mono">({databases.data.databases.length})</span>}</h3>
        <span style={{ display: 'flex', gap: 4 }}>
          <button type="button" className="btn btn--secondary btn--compact" onClick={() => setEditing({ mode: 'find' })}>
            <Search size={14} aria-hidden /> Find databases
          </button>
          <button type="button" className="btn btn--primary btn--compact" onClick={() => setEditing({ mode: 'add' })}>
            <Plus size={14} aria-hidden /> Add database
          </button>
        </span>
      </div>
      <p className="muted small">
        PMDA lists the flights of every enabled database together. Imports go to the default unless the uploader picks another; read-only databases
        are archives that PMDA never changes.
      </p>
      {databases.isPending && <p className="muted small">Checking databases…</p>}
      {databases.isError && (
        <p className="notice notice--error" role="alert">
          Could not load databases: {errorMessage(databases.error)}
        </p>
      )}
      {databases.data?.registryError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{databases.data.registryError}</span>
        </p>
      )}
      {action.isError && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(action.error)}</span>
        </p>
      )}
      {databases.data && databases.data.databases.length === 0 && (
        <div className="empty">
          <Database size={20} aria-hidden />
          <p>No databases yet. Find the PMDA databases on a SQL Server, or add one to start importing flights.</p>
          <button type="button" className="btn btn--secondary btn--compact" onClick={() => setEditing({ mode: 'find' })}>
            <Search size={14} aria-hidden /> Find databases
          </button>
        </div>
      )}
      {databases.data && databases.data.databases.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Server / database</th>
                <th scope="col">State</th>
                <th scope="col">Flights</th>
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {databases.data.databases.map((d) => (
                <tr key={d.id}>
                  <td>
                    {d.name} <span className="muted mono">{d.id}</span>
                    {d.isImportDefault && <span className="chip chip--tag">Import default</span>}
                    {d.readOnly && <span className="chip chip--tag">Read-only</span>}
                  </td>
                  <td className="mono">
                    {d.settings.server} / {d.settings.database}
                    <span className="muted"> · {d.settings.authentication === 'Windows' ? 'Windows' : `SQL login ${d.settings.user ?? ''}`}</span>
                  </td>
                  <td>
                    <span className={`chip ${STATE_CHIP[d.state]}`} title={d.message ?? undefined}>
                      {d.state}
                    </span>
                    {d.message && <div className="muted small">{d.message}</div>}
                  </td>
                  <td className="mono">{d.flightCount === null ? '—' : formatNumber(d.flightCount)}</td>
                  <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                    {!d.isImportDefault && !d.readOnly && d.enabled && (
                      <button
                        type="button"
                        className="btn btn--ghost btn--compact"
                        disabled={action.isPending}
                        onClick={() => action.mutate(() => setDefaultDatabase(d.id))}
                      >
                        Make default
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn--ghost btn--compact"
                      disabled={action.isPending}
                      onClick={() => action.mutate(() => updateDatabase(d.id, toRequest(d, { enabled: !d.enabled })))}
                    >
                      {d.enabled ? 'Disable' : 'Enable'}
                    </button>
                    <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Edit ${d.name}`} title="Edit" onClick={() => setEditing({ mode: 'edit', database: d })}>
                      <Pencil size={14} aria-hidden />
                    </button>
                    <button
                      type="button"
                      className="btn btn--ghost btn--icon btn--compact"
                      aria-label={`Remove ${d.name}`}
                      title="Remove from the list"
                      onClick={() => setEditing({ mode: 'remove', database: d })}
                    >
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Dialog
        open={editing?.mode === 'add' || editing?.mode === 'edit'}
        title={editing?.mode === 'edit' ? `Edit ${editing.database.name}` : 'Add database'}
        icon={<Database size={18} aria-hidden />}
        onClose={() => setEditing(null)}
      >
        {(editing?.mode === 'add' || editing?.mode === 'edit') && (
          <DatabaseForm
            key={editing.mode === 'edit' ? editing.database.id : 'add'}
            initial={editing.mode === 'edit' ? editing.database : null}
            onCancel={() => setEditing(null)}
            onSaved={(list) => {
              applied(list)
              setEditing(null)
            }}
          />
        )}
      </Dialog>
      <Dialog wide open={editing?.mode === 'find'} title="Find databases" icon={<Search size={18} aria-hidden />} onClose={() => setEditing(null)}>
        {editing?.mode === 'find' && (
          <FindDatabases defaultServer={databases.data?.databases[0]?.settings.server ?? 'localhost'} onAdded={applied} />
        )}
      </Dialog>
      <Dialog
        open={editing?.mode === 'remove'}
        title={editing?.mode === 'remove' ? `Remove ${editing.database.name}?` : ''}
        icon={<Trash2 size={18} aria-hidden />}
        onClose={() => setEditing(null)}
      >
        {editing?.mode === 'remove' && (
          <>
            <p className="small">
              PMDA stops listing its flights. The database itself, <span className="mono">{editing.database.settings.database}</span> on{' '}
              <span className="mono">{editing.database.settings.server}</span>, and its flights are left untouched: add it again to see them.
            </p>
            <div className="dialog__actions">
              <button type="button" className="btn btn--secondary" onClick={() => setEditing(null)}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn--danger"
                disabled={action.isPending}
                onClick={() => {
                  const id = editing.database.id
                  action.mutate(() => removeDatabase(id), { onSuccess: () => setEditing(null) })
                }}
              >
                <Trash2 size={14} aria-hidden /> Remove from the list
              </button>
            </div>
          </>
        )}
      </Dialog>
    </div>
  )
}

/** The entry as it is, with some fields changed (a blank password keeps the stored one). */
function toRequest(d: DatabaseInfo, change: { enabled?: boolean }) {
  return {
    id: d.id,
    name: d.name,
    server: d.settings.server,
    database: d.settings.database,
    authentication: d.settings.authentication,
    user: d.settings.user,
    password: null,
    encrypt: d.settings.encrypt,
    trustServerCertificate: d.settings.trustServerCertificate,
    readOnly: d.readOnly,
    enabled: change.enabled ?? d.enabled,
    createIfMissing: false,
  }
}
