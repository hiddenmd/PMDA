import type { ConnectionStatus } from '../machines/connectionMachine'

const labels: Record<ConnectionStatus, string> = {
  connected: 'Live',
  connecting: 'Connecting…',
  reconnecting: 'Reconnecting…',
  offline: 'Offline — retrying',
}

export function ConnectionBadge({ status }: { status: ConnectionStatus }) {
  return (
    <span className={`badge badge--${status}`} role="status" aria-live="polite">
      <span className="badge__bars" aria-hidden>
        <i />
        <i />
        <i />
      </span>
      {labels[status]}
    </span>
  )
}
