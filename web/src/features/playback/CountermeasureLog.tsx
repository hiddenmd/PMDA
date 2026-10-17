import { Flame, Sparkles } from 'lucide-react'
import { memo } from 'react'
import { formatElapsed } from '../../lib/format'
import type { CountermeasureEntry } from './playbackView'

const KINDS: Record<number, { label: string; Icon: typeof Flame }> = {
  1: { label: 'Chaff', Icon: Sparkles },
  2: { label: 'Flare', Icon: Flame },
}

export const CountermeasureLog = memo(function CountermeasureLog({ entries }: { entries: CountermeasureEntry[] }) {
  if (entries.length === 0) return <p className="muted">None dispensed yet.</p>

  return (
    <ol className="cm-log" aria-label="Most recent countermeasures, newest first">
      {entries.map((entry, i) => {
        const kind = KINDS[entry.kind] ?? { label: `Kind ${entry.kind}`, Icon: Sparkles }
        return (
          <li key={`${entry.offsetMs}-${entry.kind}-${i}`}>
            <span>
              <kind.Icon size={14} aria-hidden /> {kind.label} ×{entry.count}
            </span>
            <span className="mono muted">{formatElapsed(entry.offsetMs)}</span>
          </li>
        )
      })}
    </ol>
  )
})
