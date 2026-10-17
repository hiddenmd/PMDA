const MINUTE = 60_000
const STEPS_MS = [10_000, 30_000, MINUTE, 2 * MINUTE, 5 * MINUTE, 10 * MINUTE, 15 * MINUTE, 30 * MINUTE, 60 * MINUTE, 120 * MINUTE, 180 * MINUTE, 360 * MINUTE]

export interface AxisTick {
  offsetMs: number
  /** UTC clock at the tick, e.g. 09:40Z (with seconds when ticks are under a minute apart). */
  label: string
}

const pad = (n: number) => String(n).padStart(2, '0')

/**
 * Clock ticks between the flight's start and end at a round UTC interval (every 5 min falls on :00,
 * :05, …), at most `maxTicks` of them, leaving out ticks too close to either end (where the start and
 * end times are shown).
 */
export function timeTicks(startEpochMs: number, durationMs: number, maxTicks: number): AxisTick[] {
  if (durationMs <= 0 || maxTicks < 1) return []
  const step = STEPS_MS.find((s) => durationMs / s <= maxTicks) ?? Math.ceil(durationMs / maxTicks / (60 * MINUTE)) * 60 * MINUTE
  const margin = durationMs * 0.06
  const ticks: AxisTick[] = []
  for (let t = Math.ceil(startEpochMs / step) * step; t < startEpochMs + durationMs; t += step) {
    const offsetMs = t - startEpochMs
    if (offsetMs < margin || offsetMs > durationMs - margin) continue
    const d = new Date(t)
    ticks.push({ offsetMs, label: `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}${step < MINUTE ? `:${pad(d.getUTCSeconds())}` : ''}Z` })
  }
  return ticks
}
