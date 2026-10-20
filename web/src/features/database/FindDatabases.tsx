import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { errorMessage } from '../../api/auth'
import { addDatabase, discoverDatabases, type DatabaseList, type DiscoverRequest, type DiscoveredDatabase } from '../../api/databases'
import { PasswordField } from '../../auth/LoginScreen'
import { AlertCircle, Check, Plus, Search } from '../../components/icons'
import { formatNumber } from '../../lib/format'

interface FindDatabasesProps {
  /** Where to look first: the server of the databases PMDA already uses. */
  defaultServer?: string
  onAdded: (list: DatabaseList) => void
}

/** Searches a SQL Server for PMDA databases and adds the ones PMDA doesn't use yet, one by one or all at once. */
export function FindDatabases({ defaultServer = 'localhost', onAdded }: FindDatabasesProps) {
  const [server, setServer] = useState(defaultServer)
  const [authentication, setAuthentication] = useState<DiscoverRequest['authentication']>('Windows')
  const [user, setUser] = useState('')
  const [password, setPassword] = useState('')
  const [readOnly, setReadOnly] = useState<ReadonlySet<string>>(new Set())
  const sqlLogin = authentication === 'SqlLogin'

  const connection = (): DiscoverRequest => ({
    server: server.trim(),
    authentication,
    user: sqlLogin ? user.trim() : null,
    password: sqlLogin ? password : null,
    encrypt: true,
    trustServerCertificate: true,
  })
  const search = useMutation({ mutationFn: () => discoverDatabases(connection()) })

  const add = useMutation({
    mutationFn: async (databases: DiscoveredDatabase[]) => {
      let list: DatabaseList | null = null
      // One at a time: each add prepares its database and saves the list.
      for (const d of databases) {
        list = await addDatabase({
          ...connection(),
          id: null,
          name: d.suggestedName,
          database: d.name,
          readOnly: readOnly.has(d.name),
          enabled: true,
          createIfMissing: false,
        })
      }
      return list
    },
    onSuccess: (list) => {
      if (list) onAdded(list)
      search.mutate() // refresh: the added ones now show as in the list
    },
  })

  const found = search.data?.databases ?? []
  const addable = found.filter((d) => d.isPmda && !d.registeredId && d.canAdd)
  const addableNonTest = addable.filter((d) => !d.isTest)

  const submit = (e: FormEvent) => {
    e.preventDefault()
    add.reset()
    search.mutate()
  }

  return (
    <div className="find-databases">
      <form className="find-databases__form" onSubmit={submit}>
        <label className="field">
          <span>SQL Server</span>
          <input className="input" value={server} onChange={(e) => setServer(e.target.value)} placeholder="localhost, sql01\PMDA or host,1433" />
        </label>
        <fieldset className="choice-group">
          <legend>Sign in with</legend>
          <label className="check-row">
            <input type="radio" className="checkbox" name="find-auth" checked={!sqlLogin} onChange={() => setAuthentication('Windows')} />
            Windows authentication
          </label>
          <label className="check-row">
            <input type="radio" className="checkbox" name="find-auth" checked={sqlLogin} onChange={() => setAuthentication('SqlLogin')} />
            SQL Server login
          </label>
        </fieldset>
        {sqlLogin && (
          <>
            <label className="field">
              <span>Login</span>
              <input className="input" autoComplete="off" value={user} onChange={(e) => setUser(e.target.value)} />
            </label>
            <PasswordField label="Password" autoComplete="new-password" value={password} onChange={setPassword} />
          </>
        )}
        <div className="form-actions">
          <button type="submit" className="btn btn--primary" disabled={search.isPending || !server.trim() || (sqlLogin && (!user.trim() || !password))}>
            <Search size={16} aria-hidden /> {search.isPending ? 'Searching…' : 'Search'}
          </button>
        </div>
      </form>

      {(search.isError || add.isError) && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(search.error ?? add.error)}</span>
        </p>
      )}
      {search.data?.error && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{search.data.error}</span>
        </p>
      )}

      {search.data && !search.data.error && (
        <>
          <div className="find-databases__head">
            <span className="muted small" role="status">
              SQL Server {search.data.serverVersion} on {search.data.server}: {found.filter((d) => d.isPmda).length} PMDA database
              {found.filter((d) => d.isPmda).length === 1 ? '' : 's'}, {addable.length} not in the list yet.
            </span>
            {addableNonTest.length > 1 && (
              <button type="button" className="btn btn--secondary btn--compact" disabled={add.isPending} onClick={() => add.mutate(addableNonTest)}>
                <Plus size={14} aria-hidden /> Add all {addableNonTest.length}
              </button>
            )}
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Database</th>
                  <th scope="col">Flights</th>
                  <th scope="col">PMDA</th>
                  <th scope="col">Read-only</th>
                  <th scope="col">
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {found.map((d) => {
                  const canAdd = d.isPmda && !d.registeredId && d.canAdd
                  return (
                    <tr key={d.name} className={d.isPmda ? undefined : 'muted'}>
                      <td>
                        <span className="mono">{d.name}</span>
                        {d.isTest && <span className="chip chip--tag">Test</span>}
                        {canAdd && <div className="muted small">Will be listed as “{d.suggestedName}”</div>}
                      </td>
                      <td className="mono">{d.flightCount === null ? '—' : formatNumber(d.flightCount)}</td>
                      <td>{d.isPmda ? 'Yes' : 'No'}</td>
                      <td>
                        {canAdd && (
                          <input
                            type="checkbox"
                            className="checkbox"
                            aria-label={`Add ${d.name} as read-only`}
                            checked={readOnly.has(d.name)}
                            onChange={(e) =>
                              setReadOnly((s) => {
                                const next = new Set(s)
                                if (e.target.checked) next.add(d.name)
                                else next.delete(d.name)
                                return next
                              })
                            }
                          />
                        )}
                      </td>
                      <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                        {d.registeredId ? (
                          <span className="chip chip--completed">
                            <Check size={12} aria-hidden /> In the list{d.registeredName ? `: ${d.registeredName}` : ''}
                          </span>
                        ) : canAdd ? (
                          <button type="button" className="btn btn--ghost btn--compact" aria-label={`Add ${d.name}`} disabled={add.isPending} onClick={() => add.mutate([d])}>
                            <Plus size={14} aria-hidden /> Add
                          </button>
                        ) : d.isPmda ? (
                          <span className="muted small">Name not supported</span>
                        ) : null}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}
