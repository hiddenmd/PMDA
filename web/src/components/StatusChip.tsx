import { CheckCircle2, CircleDashed, XCircle } from 'lucide-react'
import type { FlightStatus } from '../api/types'

const icons = { Completed: CheckCircle2, Importing: CircleDashed, Failed: XCircle } as const

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
