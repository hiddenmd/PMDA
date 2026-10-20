import { useMutation } from '@tanstack/react-query'
import type { DockviewApi } from 'dockview-react'
import { Database, KeyRound, LogOut, Users } from '../components/icons'
import { useState } from 'react'
import { changePassword, errorMessage } from '../api/auth'
import { Dialog } from '../components/Dialog'
import { Menu, MenuItem, MenuLabel, MenuSeparator } from '../components/Menu'
import { openPanel } from '../workspace/layouts'
import { useAuth } from './authContext'
import { PasswordField } from './LoginScreen'

export function UserMenu({ api }: { api: DockviewApi | null }) {
  const { user, isAdmin, signOut } = useAuth()
  const [changing, setChanging] = useState(false)

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
                disabled={!api}
                onSelect={() => {
                  if (api) openPanel(api, 'databases')
                  close()
                }}
              >
                Manage databases
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
