import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { DockviewApi } from 'dockview-react'
import { AlertCircle, Database, KeyRound, LogOut, Users } from '../components/icons'
import { useState } from 'react'
import { changePassword, errorMessage } from '../api/auth'
import { databaseKeys, getDatabaseStatus } from '../api/database'
import { DatabaseForm } from '../features/database/DatabaseForm'
import { Dialog } from '../components/Dialog'
import { Menu, MenuItem, MenuLabel, MenuSeparator } from '../components/Menu'
import { openPanel } from '../workspace/layouts'
import { useAuth } from './authContext'
import { PasswordField } from './LoginScreen'

export function UserMenu({ api }: { api: DockviewApi | null }) {
  const { user, isAdmin, signOut } = useAuth()
  const [changing, setChanging] = useState(false)
  const [database, setDatabase] = useState(false)

  return (
    <>
      <Menu
        label={`Signed in as ${user.displayName}`}
        trigger={
          <span className="user-button">
            <span className="avatar" aria-hidden>
              {user.displayName.slice(0, 1).toUpperCase()}
            </span>
            {user.displayName}
          </span>
        }
      >
        {(close) => (
          <>
            <MenuLabel>
              {user.userName} · {user.role}
            </MenuLabel>
            {isAdmin && (
              <MenuItem
                icon={<Users size={14} aria-hidden />}
                disabled={!api}
                onSelect={() => {
                  if (api) openPanel(api, 'users')
                  close()
                }}
              >
                Manage users
              </MenuItem>
            )}
            {isAdmin && (
              <MenuItem
                icon={<Database size={14} aria-hidden />}
                onSelect={() => {
                  close()
                  setDatabase(true)
                }}
              >
                Database…
              </MenuItem>
            )}
            <MenuItem
              icon={<KeyRound size={14} aria-hidden />}
              onSelect={() => {
                close()
                setChanging(true)
              }}
            >
              Change password…
            </MenuItem>
            <MenuSeparator />
            <MenuItem icon={<LogOut size={14} aria-hidden />} onSelect={() => void signOut()}>
              Sign out
            </MenuItem>
          </>
        )}
      </Menu>
      <Dialog open={changing} title="Change password" icon={<KeyRound size={18} aria-hidden />} onClose={() => setChanging(false)}>
        {changing && <ChangePasswordForm onDone={() => setChanging(false)} />}
      </Dialog>
      <Dialog open={database} title="Database" icon={<Database size={18} aria-hidden />} onClose={() => setDatabase(false)}>
        {database && <DatabaseSettings onDone={() => setDatabase(false)} />}
      </Dialog>
    </>
  )
}

/** The admin's view of the connection: the same form as first-run setup, prefilled from the current one. */
function DatabaseSettings({ onDone }: { onDone: () => void }) {
  const queryClient = useQueryClient()
  // Fresh each time the dialog opens: the gate's copy may be from page load.
  const status = useQuery({ queryKey: databaseKeys.status, queryFn: ({ signal }) => getDatabaseStatus(signal), staleTime: 0 })

  if (status.isPending) return <p className="muted">Loading…</p>
  if (status.isError) return <p className="notice notice--error">{errorMessage(status.error)}</p>
  return (
    <>
      <p className="notice notice--warn">
        <AlertCircle size={16} aria-hidden />
        <span>Sessions belong to the database: switching to another database signs everyone out, including you.</span>
      </p>
      <DatabaseForm
        initial={status.data.settings}
        onSaved={(next) => {
          queryClient.setQueryData(databaseKeys.status, next)
          // Sessions are checked against the new database's users: find out whether this one still counts.
          void queryClient.invalidateQueries({ queryKey: ['auth'] })
          onDone()
        }}
      />
    </>
  )
}

function ChangePasswordForm({ onDone }: { onDone: () => void }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const mismatch = repeat.length > 0 && repeat !== next
  const mutation = useMutation({ mutationFn: () => changePassword(current, next), onSuccess: onDone })

  return (
    <form
      className="login__form"
      onSubmit={(e) => {
        e.preventDefault()
        if (!mismatch) mutation.mutate()
      }}
    >
      <PasswordField label="Current password" autoComplete="current-password" value={current} onChange={setCurrent} />
      <PasswordField label="New password" autoComplete="new-password" value={next} onChange={setNext} hint="At least 8 characters. Other sessions are signed out." />
      <PasswordField label="Repeat new password" autoComplete="new-password" value={repeat} onChange={setRepeat} />
      {mismatch && <p className="field-error">The new passwords do not match.</p>}
      {mutation.isError && (
        <p className="field-error" role="alert">
          {errorMessage(mutation.error)}
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--secondary" onClick={onDone}>
          Cancel
        </button>
        <button type="submit" className="btn btn--primary" disabled={!current || !next || mismatch || mutation.isPending}>
          {mutation.isPending ? 'Saving…' : 'Change password'}
        </button>
      </div>
    </form>
  )
}
