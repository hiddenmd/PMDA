import { CheckCircle2, CircleDashed, Trash2, XCircle, type LucideIcon } from 'lucide-react'
import type { FlightStatus } from '../api/types'

/** One icon per status; a new status fails to compile until it has one. */
const icons: Record<FlightStatus, LucideIcon> = { Completed: CheckCircle2, Importing: CircleDashed, Failed: XCircle, Deleting: Trash2 }

/** Flight status with an icon and text, never colour alone. */
export function StatusChip({ status }: { status: FlightStatus }) {
  const Icon = icons[status]
  return (
    <span className={`chip chip--${status.toLowerCase()}`}>
      <Icon size={12} aria-hidden />
      {status}
    </span>
  )
}
