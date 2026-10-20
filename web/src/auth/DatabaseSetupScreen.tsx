import { useQueryClient } from '@tanstack/react-query'
import { databaseKeys, type DatabaseStatus } from '../api/database'
import { AlertCircle, RotateCcw } from '../components/icons'
import { DatabaseForm } from '../features/database/DatabaseForm'
import { authKeys } from './authContext'
import { AuthCard } from './LoginScreen'

/**
 * Shown instead of the login screen while PMDA has no working database. On the server machine it offers the
 * connection form; elsewhere it explains where to set it up.
 */
export function DatabaseSetupScreen({ status }: { status: DatabaseStatus }) {
  const queryClient = useQueryClient()
  const unreachable = status.state === 'Unreachable'

  if (!status.canConfigure) {
    return (
      <AuthCard title={unreachable ? 'Database unavailable' : 'Set up PMDA'} subtitle="Database">
        <p className="notice notice--warn">
          <AlertCircle size={16} aria-hidden />
          <span>
            {unreachable
              ? "PMDA can't reach its database right now. Try again shortly, or check the connection on the server machine."
              : "PMDA isn't connected to a database yet. Set it up on the server machine."}
          </span>
        </p>
        <button type="button" className="btn btn--secondary" onClick={() => void queryClient.invalidateQueries({ queryKey: databaseKeys.all })}>
          <RotateCcw size={16} aria-hidden /> Retry
        </button>
      </AuthCard>
    )
  }

  return (
    <AuthCard title="Connect PMDA to SQL Server" subtitle={unreachable ? 'The database is unavailable · server machine only' : 'First run · server machine only'}>
      {status.message && (
        <p className="notice notice--warn">
          <AlertCircle size={16} aria-hidden />
          <span>{status.message}</span>
        </p>
      )}
      <DatabaseForm
        initial={status.settings}
        onSaved={(next) => {
          queryClient.setQueryData(databaseKeys.status, next)
          void queryClient.invalidateQueries({ queryKey: authKeys.state })
        }}
      />
    </AuthCard>
  )
}
