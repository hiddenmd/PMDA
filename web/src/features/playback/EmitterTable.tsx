import { memo } from 'react'
import type { ThreatSymbol } from '../rwr/symbology'
import type { ActiveEmitter } from './playbackView'

export interface EmitterRow {
  emitter: ActiveEmitter
  threat: ThreatSymbol
}

const LETHALITY_LABEL = { Critical: 'Critical', Lethal: 'Lethal', NonLethal: 'Non-lethal' } as const

/** Emitters at the playhead with their RWR classification, in the same priority order as the scope. */
export const EmitterTable = memo(function EmitterTable({ rows, persistenceMs }: { rows: EmitterRow[]; persistenceMs: number }) {
  if (rows.length === 0) return <p className="muted small" style={{ padding: 'var(--space-md)' }}>No emitters detected at this moment.</p>

  return (
    <div className="table-wrap emitters-table">
      <table className="table">
        <caption className="visually-hidden">Emitters seen at the playhead, highest priority first</caption>
        <thead>
          <tr>
            <th scope="col">Threat</th>
            <th scope="col">Class</th>
            <th scope="col">Lethality</th>
            <th scope="col">Track</th>
            <th scope="col" className="num">ID</th>
            <th scope="col" className="num">Freq MHz</th>
            <th scope="col" className="num">PRI µs</th>
            <th scope="col" className="num">PW µs</th>
            <th scope="col" className="num">Brg °</th>
            <th scope="col" className="num">Amp dB</th>
            <th scope="col">Age</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(({ emitter: e, threat: t }) => {
            const freshness = Math.max(0, 1 - e.ageMs / persistenceMs)
            return (
              <tr key={e.emitterId} className={t.priority ? 'is-current' : undefined} title={t.description ?? undefined}>
                <td className="mono">
                  <strong>{t.code}</strong>
                  {t.isNew && <span className="chip chip--importing" style={{ marginLeft: 6 }}>NEW</span>}
                </td>
                <td>{t.threatClass}</td>
                <td className={t.lethality === 'Critical' ? 'threat-critical' : undefined}>{LETHALITY_LABEL[t.lethality]}</td>
                <td>{t.locked ? <span className="threat-critical">Tracking</span> : <span className="muted">Search</span>}</td>
                <td className="num">{e.emitterId}</td>
                <td className="num">{e.frequencyMHz.toFixed(0)}</td>
                <td className="num">{e.priUs.toFixed(0)}</td>
                <td className="num">{e.pulseWidthUs.toFixed(2)}</td>
                <td className="num">{e.bearingDeg.toFixed(0).padStart(3, '0')}</td>
                <td className="num">{e.amplitudeDb.toFixed(1)}</td>
                <td>
                  <span className="age-bar" role="meter" aria-label={`Last seen ${(e.ageMs / 1000).toFixed(1)} s ago`} aria-valuenow={Math.round(freshness * 100)} aria-valuemin={0} aria-valuemax={100}>
                    <span style={{ width: `${freshness * 100}%` }} />
                  </span>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
})
