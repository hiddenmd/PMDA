import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, ShieldCheck } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { errorMessage, setupAdministrator } from '../api/auth'
import { authKeys } from './authContext'
import { AuthCard, PasswordField } from './LoginScreen'

/** First run: no users exist yet. Only offered on the server machine itself. */
export function SetupScreen({ allowed }: { allowed: boolean }) {
  const queryClient = useQueryClient()
  const [userName, setUserName] = useState('admin')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const mismatch = confirm.length > 0 && confirm !== password
  const mutation = useMutation({
    mutationFn: () => setupAdministrator(userName.trim(), displayName.trim(), password),
    onSuccess: (user) => queryClient.setQueryData(authKeys.me, user),
  })

  if (!allowed) {
    return (
      <AuthCard title="Set up PMDA" subtitle="First run">
        <p className="notice notice--warn">
          <AlertCircle size={16} aria-hidden />
          <span>No users exist yet. For security, the first administrator can only be created on the server machine itself. Open PMDA there to continue.</span>
        </p>
      </AuthCard>
    )
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!mismatch && password) mutation.mutate()
  }

  return (
    <AuthCard title="Create the administrator" subtitle="First run · only shown until the first account exists">
      <form className="login__form" onSubmit={submit} noValidate>
        <label className="field">
          <span>User name</span>
          <input className="input" autoComplete="username" value={userName} onChange={(e) => setUserName(e.target.value)} required />
        </label>
        <label className="field">
          <span>Display name</span>
          <input className="input" autoComplete="name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="e.g. Ops administrator" />
        </label>
        <PasswordField label="Password" autoComplete="new-password" value={password} onChange={setPassword} hint="At least 8 characters, not the user name." />
        <PasswordField label="Repeat password" autoComplete="new-password" value={confirm} onChange={setConfirm} />
        {mismatch && <p className="field-error">The passwords do not match.</p>}
        {mutation.isError && (
          <p className="notice notice--error" role="alert">
            <AlertCircle size={16} aria-hidden />
            <span>{errorMessage(mutation.error)}</span>
          </p>
        )}
        <button type="submit" className="btn btn--primary" disabled={mutation.isPending || !password || mismatch}>
          <ShieldCheck size={16} aria-hidden /> {mutation.isPending ? 'Creating…' : 'Create administrator'}
        </button>
      </form>
    </AuthCard>
  )
}
