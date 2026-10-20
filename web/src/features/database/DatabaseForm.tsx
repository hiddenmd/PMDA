import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { errorMessage } from '../../api/auth'
import { addDatabase, testDatabase, updateDatabase, type DatabaseEntryRequest, type DatabaseInfo, type DatabaseList } from '../../api/databases'
import { PasswordField } from '../../auth/LoginScreen'
import { AlertCircle, Check, Database } from '../../components/icons'

interface DatabaseFormProps {
  /** The database being changed; omitted to add one. Never carries a password. */
  initial?: DatabaseInfo | null
  onSaved: (list: DatabaseList) => void
  onCancel?: () => void
}

/** A flight database's name, connection and role (read-only, enabled): test without saving, or save it. */
export function DatabaseForm({ initial, onSaved, onCancel }: DatabaseFormProps) {
  const settings = initial?.settings
  const [name, setName] = useState(initial?.name ?? '')
  const [server, setServer] = useState(settings?.server ?? 'localhost')
  const [database, setDatabase] = useState(settings?.database ?? '')
  const [authentication, setAuthentication] = useState<DatabaseEntryRequest['authentication']>(settings?.authentication ?? 'Windows')
  const [user, setUser] = useState(settings?.user ?? '')
  const [password, setPassword] = useState('')
  const [encrypt, setEncrypt] = useState(settings?.encrypt ?? true)
  const [trust, setTrust] = useState(settings?.trustServerCertificate ?? true)
  const [readOnly, setReadOnly] = useState(initial?.readOnly ?? false)
  const [enabled, setEnabled] = useState(initial?.enabled ?? true)
  const [create, setCreate] = useState(false)
  const sqlLogin = authentication === 'SqlLogin'
  const passwordStored = !!settings?.hasPassword && settings.user === user.trim() && settings.server === server.trim()

  const request = (): DatabaseEntryRequest => ({
    id: initial?.id ?? null,
    name: name.trim(),
    server: server.trim(),
    database: database.trim(),
    authentication,
    user: sqlLogin ? user.trim() : null,
    password: sqlLogin && password ? password : null,
    encrypt,
    trustServerCertificate: trust,
    readOnly,
    enabled,
    createIfMissing: create && !readOnly,
  })
  const test = useMutation({ mutationFn: () => testDatabase(request()) })
  const save = useMutation({
    mutationFn: () => (initial ? updateDatabase(initial.id, request()) : addDatabase(request())),
    onSuccess: (list) => onSaved(list),
  })
  const busy = test.isPending || save.isPending
  const connectionMissing = !server.trim() || !database.trim() || (sqlLogin && (!user.trim() || (!password && !passwordStored)))
  const error = test.error ?? save.error

  const submit = (e: FormEvent) => {
    e.preventDefault()
    test.reset()
    save.mutate()
  }

  return (
    <form className="login__form" onSubmit={submit} noValidate>
      <label className="field">
        <span>Name</span>
        <input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Exercise 2026 or Archive" required />
      </label>
      <label className="field">
        <span>Server</span>
        <input className="input" value={server} onChange={(e) => setServer(e.target.value)} placeholder="localhost, sql01\PMDA or host,1433" required />
      </label>
      <label className="field">
        <span>Database</span>
        <input className="input" value={database} onChange={(e) => setDatabase(e.target.value)} required />
      </label>
      <fieldset className="choice-group">
        <legend>Sign in with</legend>
        <label className="check-row">
          <input type="radio" className="checkbox" name="db-auth" checked={!sqlLogin} onChange={() => setAuthentication('Windows')} />
          Windows authentication (the account PMDA runs as)
        </label>
        <label className="check-row">
          <input type="radio" className="checkbox" name="db-auth" checked={sqlLogin} onChange={() => setAuthentication('SqlLogin')} />
          SQL Server login
        </label>
      </fieldset>
      {sqlLogin && (
        <>
          <label className="field">
            <span>Login</span>
            <input className="input" autoComplete="off" value={user} onChange={(e) => setUser(e.target.value)} required />
          </label>
          <PasswordField
            label="Password"
            autoComplete="new-password"
            value={password}
            onChange={setPassword}
            hint={passwordStored ? 'Stored and encrypted. Leave blank to keep it.' : 'Stored encrypted on the server (Windows DPAPI).'}
          />
        </>
      )}
      <label className="check-row">
        <input type="checkbox" className="checkbox" checked={encrypt} onChange={(e) => setEncrypt(e.target.checked)} />
        Encrypt the connection
      </label>
      <label className="check-row">
        <input type="checkbox" className="checkbox" checked={trust} onChange={(e) => setTrust(e.target.checked)} />
        Trust the server certificate
      </label>
      <label className="check-row">
        <input type="checkbox" className="checkbox" checked={readOnly} onChange={(e) => setReadOnly(e.target.checked)} />
        Read-only (an archive: no imports or deletes, and PMDA never changes it)
      </label>
      <label className="check-row">
        <input type="checkbox" className="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
        Enabled (its flights are listed)
      </label>
      {!readOnly && (
        <label className="check-row">
          <input type="checkbox" className="checkbox" checked={create} onChange={(e) => setCreate(e.target.checked)} />
          Create the database if it doesn&apos;t exist
        </label>
      )}

      {test.data && (
        <p className={`notice ${test.data.ok ? 'notice--ok' : 'notice--error'}`} role="status">
          {test.data.ok ? <Check size={16} aria-hidden /> : <AlertCircle size={16} aria-hidden />}
          <span>{test.data.ok ? `Connected · SQL Server ${test.data.serverVersion}${test.data.message ? `. ${test.data.message}` : ''}` : test.data.message}</span>
        </p>
      )}
      {error && (
        <p className="notice notice--error" role="alert">
          <AlertCircle size={16} aria-hidden />
          <span>{errorMessage(error)}</span>
        </p>
      )}
      <div className="form-actions">
        {onCancel && (
          <button type="button" className="btn btn--ghost" onClick={onCancel}>
            Cancel
          </button>
        )}
        <button
          type="button"
          className="btn btn--secondary"
          disabled={busy || connectionMissing}
          onClick={() => {
            save.reset()
            test.mutate()
          }}
        >
          {test.isPending ? 'Testing…' : 'Test connection'}
        </button>
        <button type="submit" className="btn btn--primary" disabled={busy || connectionMissing || !name.trim()}>
          <Database size={16} aria-hidden /> {save.isPending ? 'Saving…' : initial ? 'Save changes' : 'Add database'}
        </button>
      </div>
    </form>
  )
}
