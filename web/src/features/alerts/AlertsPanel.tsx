import { useVirtualizer } from '@tanstack/react-virtual'
import { Crosshair, OctagonAlert, Radar, TriangleAlert, Volume2 } from '../../components/icons'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { Alert } from '../../api/types'
import { formatElapsed, formatNumber, formatUtcClock } from '../../lib/format'
import { NoPlayback } from '../../workspace/panels/EmptyPanel'
import { useWorkspace, type PlaybackActor } from '../../workspace/workspaceContext'
import { usePlaybackState } from '../playback/usePlayback'
import { ALERTS, clockPosition, lastAlertIndex, SEVERITY_RANK, type AlertSeverity } from './alerts'
import { AlertSound } from './alertSound'
import { useAlerts } from './useAlerts'

const FILTERS: { min: AlertSeverity; label: string }[] = [
  { min: 'advisory', label: 'All' },
  { min: 'warning', label: 'Warnings' },
  { min: 'critical', label: 'Missiles' },
]

const ICONS = { critical: OctagonAlert, warning: TriangleAlert, advisory: Radar }

export function AlertsPanel() {
  const { playback, flight, flightLoading } = useWorkspace()
  if (!playback || !flight) return <NoPlayback loading={flightLoading} />
  return <Alerts flightId={flight.id} startUtc={playback.startUtc} actor={playback.actor} />
}

function Alerts({ flightId, startUtc, actor }: { flightId: string; startUtc: string; actor: PlaybackActor }) {
  const alerts = useAlerts(flightId)
  const playback = usePlaybackState(actor)
  const [min, setMin] = useState<AlertSeverity>('advisory')
  const [follow, setFollow] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  const sound = useRef<AlertSound | null>(null)
  useEffect(() => () => sound.current?.dispose(), [])

  const rows = useMemo(() => (alerts.data ?? []).filter((a) => SEVERITY_RANK[ALERTS[a.kind].severity] >= SEVERITY_RANK[min]), [alerts.data, min])
  const counts = useMemo(() => {
    const c = new Map<AlertSeverity, number>()
    for (const a of alerts.data ?? []) c.set(ALERTS[a.kind].severity, (c.get(ALERTS[a.kind].severity) ?? 0) + 1)
    return c
  }, [alerts.data])

  const rowHeight = Math.round(26 * (Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale')) || 1))
  // oxlint-disable-next-line react/incompatible-library
  const virtualizer = useVirtualizer({ count: rows.length, getScrollElement: () => scrollRef.current, estimateSize: () => rowHeight, overscan: 12 })
  const current = lastAlertIndex(rows, playback.positionMs)

  useEffect(() => {
    if (follow && current >= 0) virtualizer.scrollToIndex(current, { align: 'center' })
  }, [follow, current, virtualizer])

  const preview = (a: Alert) => {
    sound.current ??= new AlertSound()
    sound.current.play(a, true)
  }

  const startEpoch = Date.parse(startUtc)
  const grid = '28px 92px 84px minmax(120px, 1fr) 72px 136px 36px'

  return (
    <div className="panel-body panel-body--flush">
      <div className="panel-toolbar">
        <div className="segmented" role="group" aria-label="Show alerts">
          {FILTERS.map((f) => {
            const n = [...counts].filter(([s]) => SEVERITY_RANK[s] >= SEVERITY_RANK[f.min]).reduce((sum, [, v]) => sum + v, 0)
            return (
              <button key={f.min} type="button" aria-pressed={min === f.min} onClick={() => setMin(f.min)}>
                {f.label} <span className="count">{formatNumber(n)}</span>
              </button>
            )
          })}
        </div>
        <span className="panel-toolbar__spacer" />
        <button type="button" className={`btn btn--compact ${follow ? 'btn--primary' : 'btn--secondary'}`} aria-pressed={follow} onClick={() => setFollow((f) => !f)}>
          <Crosshair size={14} aria-hidden /> Follow playhead
        </button>
      </div>

      <div className="vtable" ref={scrollRef} role="table" aria-label="Audio alerts" aria-rowcount={rows.length + 1}>
        <div className="vtable__head" role="row" style={{ gridTemplateColumns: grid }}>
          <div role="columnheader" className="vtable__cell">
            <span className="visually-hidden">Severity</span>
          </div>
          <div role="columnheader" className="vtable__cell">
            Elapsed
          </div>
          <div role="columnheader" className="vtable__cell">
            UTC
          </div>
          <div role="columnheader" className="vtable__cell">
            Alert
          </div>
          <div role="columnheader" className="vtable__cell num" style={{ textAlign: 'right' }}>
            Emitter
          </div>
          <div role="columnheader" className="vtable__cell">
            From
          </div>
          <div role="columnheader" className="vtable__cell">
            <span className="visually-hidden">Listen</span>
          </div>
        </div>

        {alerts.isPending && <div className="empty">Loading alerts…</div>}
        {alerts.isError && <div className="empty">Could not load alerts: {alerts.error.message}</div>}
        {alerts.data && rows.length === 0 && (
          <div className="empty">{alerts.data.length === 0 ? 'This recording has no audio alerts.' : 'No alerts of this kind.'}</div>
        )}

        <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
          {virtualizer.getVirtualItems().map((item) => {
            const a = rows[item.index]
            const spec = ALERTS[a.kind]
            const Icon = ICONS[spec.severity]
            return (
              <div
                key={item.key}
                role="row"
                aria-rowindex={item.index + 2}
                className={`vtable__row${item.index === current ? ' is-current' : ''}`}
                style={{ top: item.start, height: item.size, gridTemplateColumns: grid }}
                onClick={() => playback.seek(a.offsetMs)}
                title="Click to move the playhead here"
              >
                <div role="cell" className={`vtable__cell alert-severity alert-severity--${spec.severity}`}>
                  <Icon size={14} aria-label={spec.severity} />
                </div>
                <div role="cell" className="vtable__cell mono">
                  {formatElapsed(a.offsetMs)}
                </div>
                <div role="cell" className="vtable__cell mono muted">
                  {formatUtcClock(new Date(startEpoch + a.offsetMs))}
                </div>
                <div role="cell" className={`vtable__cell${spec.severity === 'critical' ? ' threat-critical' : ''}`}>
                  {spec.label}
                </div>
                <div role="cell" className="vtable__cell num" style={{ textAlign: 'right' }}>
                  {a.emitterId ?? '—'}
                </div>
                <div role="cell" className="vtable__cell mono">
                  {a.bearingDeg === null ? '—' : `${clockPosition(a.bearingDeg)} ${a.bearingDeg.toFixed(0).padStart(3, '0')}°`}
                </div>
                <div role="cell" className="vtable__cell">
                  <button
                    type="button"
                    className="btn btn--ghost btn--icon btn--compact"
                    aria-label={`Listen to ${spec.label}`}
                    title="Listen"
                    onClick={(e) => {
                      e.stopPropagation()
                      preview(a)
                    }}
                  >
                    <Volume2 size={14} aria-hidden />
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
