import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, Eye, EyeOff, LogIn, Radar } from 'lucide-react'
import { useState, type FormEvent, type ReactNode } from 'react'
import { errorMessage, login } from '../api/auth'
import { authKeys } from './authContext'

export function LoginScreen() {
  const queryClient = useQueryClient()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const mutation = useMutation({
    mutationFn: () => login(userName.trim(), password),
    onSuccess: (user) => queryClient.setQueryData(authKeys.me, user),
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (userName.trim() && password) mutation.mutate()
  }

  return (
    <AuthCard title="Sign in" subtitle="Post-Mission Data Analysis">
      <form className="login__form" onSubmit={submit} noValidate>
        <label className="field">
          <span>User name</span>
          <input className="input" name="username" autoComplete="username" autoFocus value={userName} onChange={(e) => setUserName(e.target.value)} required />
        </label>
        <PasswordField label="Password" autoComplete="current-password" value={password} onChange={setPassword} />
        {mutation.isError && (
          <p className="notice notice--error" role="alert">
            <AlertCircle size={16} aria-hidden />
            <span>{errorMessage(mutation.error)}</span>
          </p>
        )}
        <button type="submit" className="btn btn--primary" disabled={mutation.isPending || !userName.trim() || !password}>
          <LogIn size={16} aria-hidden /> {mutation.isPending ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </AuthCard>
  )
}

export function AuthCard({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <main className="login">
      <div className="login__card">
        <div className="login__brand">
          <Radar size={28} aria-hidden />
          <div>
            <h1>PMDA</h1>
            <p className="muted small">{subtitle}</p>
          </div>
        </div>
        <h2>{title}</h2>
        {children}
        <p className="login__foot">Access is logged. Use only accounts issued to you.</p>
      </div>
    </main>
  )
}

interface PasswordFieldProps {
  label: string
  value: string
  onChange: (v: string) => void
  autoComplete: 'current-password' | 'new-password'
  hint?: string
}

export function PasswordField({ label, value, onChange, autoComplete, hint }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false)
  return (
    <label className="field">
      <span>{label}</span>
      <span className="input-with-icon">
        <input
          className="input"
          type={visible ? 'text' : 'password'}
          autoComplete={autoComplete}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          style={{ paddingRight: 36, paddingLeft: 8 }}
          required
        />
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          style={{ position: 'absolute', right: 2 }}
          aria-label={visible ? 'Hide password' : 'Show password'}
          title={visible ? 'Hide password' : 'Show password'}
          onClick={() => setVisible((v) => !v)}
        >
          {visible ? <EyeOff size={14} aria-hidden /> : <Eye size={14} aria-hidden />}
        </button>
      </span>
      {hint && <span className="muted small">{hint}</span>}
    </label>
  )
}
