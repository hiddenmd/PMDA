import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { errorMessage } from '../../api/auth'
import { saveDatabase, testDatabase, type DatabaseSettings, type DatabaseSetupRequest, type DatabaseStatus } from '../../api/database'
import { PasswordField } from '../../auth/LoginScreen'
import { AlertCircle, Check, Database } from '../../components/icons'

interface DatabaseFormProps {
  /** The current connection, to start from (never includes a password). */
  initial: DatabaseSettings | null | undefined
  onSaved: (status: DatabaseStatus) => void
}

/** Server, database and sign-in for PMDA's SQL Server: test without saving, or save and switch to it. */
export function DatabaseForm({ initial, onSaved }: DatabaseFormProps) {
  const [server, setServer] = useState(initial?.server ?? 'localhost')
  const [database, setDatabase] = useState(initial?.database ?? 'PMDA')
  const [authentication, setAuthentication] = useState<DatabaseSetupRequest['authentication']>(initial?.authentication ?? 'Windows')
  const [user, setUser] = useState(initial?.user ?? '')
  const [password, setPassword] = useState('')
  const [encrypt, setEncrypt] = useState(initial?.encrypt ?? true)
  const [trust, setTrust] = useState(initial?.trustServerCertificate ?? true)
  const [create, setCreate] = useState(false)
  const sqlLogin = authentication === 'SqlLogin'
  const passwordStored = !!initial?.hasPassword && initial.user === user.trim()

  const request = (): DatabaseSetupRequest => ({
    server: server.trim(),
    database: database.trim(),
    authentication,
    user: sqlLogin ? user.trim() : null,
    password: sqlLogin && password ? password : null,
    encrypt,
    trustServerCertificate: trust,
    createIfMissing: create,
  })
  const test = useMutation({ mutationFn: () => testDatabase(request()) })
  const save = useMutation({ mutationFn: () => saveDatabase(request()), onSuccess: (status) => onSaved(status) })
  const busy = test.isPending || save.isPending
  const incomplete = !server.trim() || !database.trim() || (sqlLogin && (!user.trim() || (!password && !passwordStored)))
  const error = test.error ?? save.error

  const submit = (e: FormEvent) => {
    e.preventDefault()
    test.reset()
    save.mutate()
  }

  return (
    <form className="login__form" onSubmit={submit} noValidate>
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
        <input type="checkbox" className="checkbox" checked={create} onChange={(e) => setCreate(e.target.checked)} />
        Create the database if it doesn&apos;t exist
      </label>

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
        <button
          type="button"
          className="btn btn--secondary"
          disabled={busy || incomplete}
          onClick={() => {
            save.reset()
            test.mutate()
          }}
        >
          {test.isPending ? 'Testing…' : 'Test connection'}
        </button>
        <button type="submit" className="btn btn--primary" disabled={busy || incomplete}>
          <Database size={16} aria-hidden /> {save.isPending ? 'Connecting…' : 'Save and connect'}
        </button>
      </div>
    </form>
  )
}
