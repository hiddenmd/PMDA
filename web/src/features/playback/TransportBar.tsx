import { Loader2, Pause, Play, RotateCcw, SkipBack, SkipForward } from '../../components/icons'
import type { ReactNode } from 'react'
import { formatElapsed, formatHms, formatUtcClock } from '../../lib/format'
import type { TransportStatus } from '../../machines/playbackMachine'
import { SPEEDS, type Speed } from './playbackView'

interface TransportBarProps {
  status: TransportStatus
  speed: Speed
  positionMs: number
  durationMs: number
  startUtc: string
  onToggle: () => void
  onStep: (direction: -1 | 1) => void
  onSpeed: (speed: Speed) => void
  children?: ReactNode
}

export function TransportBar({ status, speed, positionMs, durationMs, startUtc, onToggle, onStep, onSpeed, children }: TransportBarProps) {
  const playing = status === 'playing' || status === 'buffering'
  const toggleLabel = status === 'ended' ? 'Replay from start' : playing ? 'Pause' : 'Play'
  const ToggleIcon = status === 'ended' ? RotateCcw : playing ? Pause : Play

  return (
    <div className="transport__controls">
      <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Step back" title="Step back (←)" onClick={() => onStep(-1)}>
        <SkipBack size={18} aria-hidden />
      </button>
      <button type="button" className="btn btn--primary btn--icon btn--compact" aria-label={toggleLabel} title={`${toggleLabel} (Space)`} onClick={onToggle}>
        <ToggleIcon size={18} aria-hidden />
      </button>
      <button type="button" className="btn btn--ghost btn--icon btn--compact" aria-label="Step forward" title="Step forward (→)" onClick={() => onStep(1)}>
        <SkipForward size={18} aria-hidden />
      </button>

      <label className="visually-hidden" htmlFor="playback-speed">
        Playback speed
      </label>
      <select
        id="playback-speed"
        className="select select--compact"
        title="Playback speed ([ and ])"
        value={speed}
        onChange={(e) => onSpeed(Number(e.target.value) as Speed)}
      >
        {SPEEDS.map((s) => (
          <option key={s} value={s}>
            {s}×
          </option>
        ))}
      </select>

      {children}
      {status === 'buffering' && (
        <span className="transport__status" role="status">
          <Loader2 size={14} aria-hidden /> Loading…
        </span>
      )}

      <div className="transport__time" aria-live="off">
        <span title="UTC time at the playhead">{formatUtcClock(new Date(Date.parse(startUtc) + positionMs))}</span>
        <span className="muted" title="Elapsed since flight start, of the whole flight">
          <span>{formatElapsed(positionMs)}</span> / <span>{formatHms(durationMs)}</span>
        </span>
      </div>
    </div>
  )
}
