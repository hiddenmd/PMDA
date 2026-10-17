import { memo } from 'react'
import { bandOf } from './scopeGeometry'
import type { ActiveEmitter } from './playbackView'

const BAND_GLYPH: Record<string, string> = { triangle: '▲', square: '■', diamond: '◆', circle: '●' }

export const EmitterTable = memo(function EmitterTable({ emitters, persistenceMs }: { emitters: ActiveEmitter[]; persistenceMs: number }) {
  if (emitters.length === 0) return <p className="muted">No emitters detected at this moment.</p>

  return (
    <div className="table-wrap emitters-table">
      <table className="table">
        <caption className="visually-hidden">Emitters seen at the playhead, strongest first</caption>
        <thead>
          <tr>
            <th scope="col">ID</th>
            <th scope="col">Band</th>
            <th scope="col" className="num">Freq MHz</th>
            <th scope="col" className="num">PRI µs</th>
            <th scope="col" className="num">PW µs</th>
            <th scope="col" className="num">Brg °</th>
            <th scope="col" className="num">Amp dB</th>
            <th scope="col">Age</th>
          </tr>
        </thead>
        <tbody>
          {emitters.map((e) => {
            const band = bandOf(e.frequencyMHz)
            const freshness = Math.max(0, 1 - e.ageMs / persistenceMs)
            return (
              <tr key={e.emitterId}>
                <td className="mono">{e.emitterId}</td>
                <td>
                  <span aria-hidden>{BAND_GLYPH[band.shape]} </span>
                  {band.label}
                </td>
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
