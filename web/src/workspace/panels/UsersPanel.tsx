import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, ShieldCheck, UserPlus } from '../../components/icons'
import { useState } from 'react'
import { createUser, errorMessage, listUsers, resetUserPassword, updateUser, type AppUser, type UserRole } from '../../api/auth'
import { useAuth } from '../../auth/authContext'
import { PasswordField } from '../../auth/LoginScreen'
import { Dialog } from '../../components/Dialog'
import { formatUtc } from '../../lib/format'

const usersKey = ['users'] as const

type Editing = { mode: 'create' } | { mode: 'edit'; user: AppUser } | { mode: 'password'; user: AppUser } | null

/** User administration. The API enforces the Admin role; this panel only explains it to others. */
export function UsersPanel() {
  const { isAdmin, user: me } = useAuth()
  const users = useQuery({ queryKey: usersKey, queryFn: ({ signal }) => listUsers(signal), enabled: isAdmin })
  const [editing, setEditing] = useState<Editing>(null)

  if (!isAdmin) return <div className="empty">Only administrators manage users.</div>

  return (
    <div className="panel-body">
      <div className="section-title">
        <h3>Users {users.data && <span className="muted mono">({users.data.length})</span>}</h3>
        <button type="button" className="btn btn--primary btn--compact" onClick={() => setEditing({ mode: 'create' })}>
          <UserPlus size={14} aria-hidden /> Add user
        </button>
      </div>
      {users.isPending && <p className="muted small">Loading users…</p>}
      {users.isError && (
        <p className="notice notice--error" role="alert">
          Could not load users: {errorMessage(users.error)}
        </p>
      )}
      {users.data && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th scope="col">User</th>
                <th scope="col">Name</th>
                <th scope="col">Role</th>
                <th scope="col">Status</th>
                <th scope="col">Last sign-in</th>
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.data.map((u) => (
                <tr key={u.id}>
                  <td className="mono">
                    {u.userName}
                    {u.id === me.id && <span className="muted"> (you)</span>}
                  </td>
                  <td>{u.displayName}</td>
                  <td>
                    {u.role === 'Admin' && <ShieldCheck size={12} aria-hidden />} {u.role}
                  </td>
                  <td>
                    <span className={`chip ${!u.isActive ? 'chip--failed' : u.lockedOut ? 'chip--importing' : 'chip--completed'}`}>
                      {!u.isActive ? 'Deactivated' : u.lockedOut ? 'Locked out' : 'Active'}
                    </span>
                  </td>
                  <td className="muted">{u.lastLoginUtc ? formatUtc(u.lastLoginUtc) : 'Never'}</td>
                  <td style={{ textAlign: 'right' }}>
                    <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label={`Edit ${u.userName}`} title="Edit" onClick={() => setEditing({ mode: 'edit', user: u })}>
                      <Pencil size={14} aria-hidden />
                    </button>
                    <button
                      type="button"
                      className="btn btn--ghost btn--icon btn--compact"
                      aria-label={`Reset password of ${u.userName}`}
                      title="Reset password"
                      onClick={() => setEditing({ mode: 'password', user: u })}
                    >
                      <KeyRound size={14} aria-hidden />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <UserDialog editing={editing} onClose={() => setEditing(null)} />
    </div>
  )
}

function UserDialog({ editing, onClose }: { editing: Editing; onClose: () => void }) {
  const title = editing?.mode === 'create' ? 'Add user' : editing?.mode === 'edit' ? `Edit ${editing.user.userName}` : editing ? `Reset password of ${editing.user.userName}` : ''
  return (
    <Dialog open={editing !== null} title={title} onClose={onClose}>
      {editing?.mode === 'create' && <CreateForm onDone={onClose} />}
      {editing?.mode === 'edit' && <EditForm key={editing.user.id} user={editing.user} onDone={onClose} />}
      {editing?.mode === 'password' && <PasswordForm key={editing.user.id} user={editing.user} onDone={onClose} />}
    </Dialog>
  )
}

function useSave<T>(fn: () => Promise<T>, onDone: () => void) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: usersKey })
      onDone()
    },
  })
}

function CreateForm({ onDone }: { onDone: () => void }) {
  const [userName, setUserName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<UserRole>('Analyst')
  const save = useSave(() => createUser({ userName: userName.trim(), displayName: displayName.trim(), password, role }), onDone)

  return (
    <form
      className="login__form"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <label className="field">
        <span>User name</span>
        <input className="input" autoComplete="off" value={userName} onChange={(e) => setUserName(e.target.value)} required />
      </label>
      <label className="field">
        <span>Display name</span>
        <input className="input" autoComplete="off" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
      </label>
      <PasswordField label="Initial password" autoComplete="new-password" value={password} onChange={setPassword} hint="At least 8 characters. Ask the user to change it after signing in." />
      <RoleSelect value={role} onChange={setRole} />
      <Footer error={save.error} pending={save.isPending} label="Add user" disabled={!userName.trim() || !password} onCancel={onDone} />
    </form>
  )
}

function EditForm({ user, onDone }: { user: AppUser; onDone: () => void }) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState<UserRole>(user.role)
  const [isActive, setIsActive] = useState(user.isActive)
  const save = useSave(() => updateUser(user.id, { displayName: displayName.trim(), role, isActive }), onDone)

  return (
    <form
      className="login__form"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <label className="field">
        <span>Display name</span>
        <input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
      </label>
      <RoleSelect value={role} onChange={setRole} />
      <label className="filters__row" style={{ justifyContent: 'flex-start' }}>
        <input type="checkbox" className="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
        Active (can sign in)
      </label>
      <p className="muted small">Changing the role or deactivating signs the user out everywhere.</p>
      <Footer error={save.error} pending={save.isPending} label="Save" disabled={!displayName.trim()} onCancel={onDone} />
    </form>
  )
}

function PasswordForm({ user, onDone }: { user: AppUser; onDone: () => void }) {
  const [password, setPassword] = useState('')
  const save = useSave(() => resetUserPassword(user.id, password), onDone)
  return (
    <form
      className="login__form"
      onSubmit={(e) => {
        e.preventDefault()
        save.mutate()
      }}
    >
      <PasswordField label="New password" autoComplete="new-password" value={password} onChange={setPassword} hint="Also unlocks the account and signs the user out everywhere." />
      <Footer error={save.error} pending={save.isPending} label="Reset password" disabled={!password} onCancel={onDone} />
    </form>
  )
}

function RoleSelect({ value, onChange }: { value: UserRole; onChange: (r: UserRole) => void }) {
  return (
    <label className="field">
      <span>Role</span>
      <select className="select" value={value} onChange={(e) => onChange(e.target.value as UserRole)}>
        <option value="Analyst">Analyst: import, view, play back</option>
        <option value="Admin">Admin: also manage users and delete flights</option>
      </select>
    </label>
  )
}

function Footer({ error, pending, label, disabled, onCancel }: { error: unknown; pending: boolean; label: string; disabled: boolean; onCancel: () => void }) {
  return (
    <>
      {error !== null && error !== undefined && (
        <p className="field-error" role="alert">
          {errorMessage(error)}
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--secondary" onClick={onCancel}>
          Cancel
        </button>
        <button type="submit" className="btn btn--primary" disabled={disabled || pending}>
          {pending ? 'Saving…' : label}
        </button>
      </div>
    </>
  )
}
