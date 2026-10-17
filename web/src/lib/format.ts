const units = ['B', 'KB', 'MB', 'GB', 'TB']

export function formatBytes(bytes: number): string {
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(unit === 0 || value >= 100 ? 0 : 1)} ${units[unit]}`
}

const numberFormat = new Intl.NumberFormat('en-US')
export const formatNumber = (n: number) => numberFormat.format(n)

const compactFormat = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 })
/** 1.2M, 34.5K — for KPIs where the exact count is in a tooltip. */
export const formatCompact = (n: number) => compactFormat.format(n)

const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  dateStyle: 'medium',
  timeStyle: 'medium',
  timeZone: 'UTC',
})

/** Flight times are shown in UTC, as recorded. */
export const formatUtc = (iso: string | null | undefined) => (iso ? `${dateTimeFormat.format(new Date(iso))} UTC` : '—')

const shortDateFormat = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' })

const pad = (n: number) => String(n).padStart(2, '0')

/** Compact UTC date and time for dense lists, e.g. 14 Mar 2026 10:52Z. */
export function formatUtcShort(iso: string): string {
  const d = new Date(iso)
  return `${shortDateFormat.format(d)} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}Z`
}

/** Clock time of day in UTC, e.g. 09:42:13Z. */
export function formatUtcClock(date: Date): string {
  return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}Z`
}

/** Elapsed time as T+hh:mm:ss. */
export function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000))
  return `T+${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`
}

/** Human duration, e.g. 4h 10m, 12m 5s, 45s. */
export function formatDuration(ms: number): string {
  const s = Math.round(ms / 1000)
  if (s < 60) return `${s}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ${s % 60}s`
  return `${Math.floor(m / 60)}h ${m % 60}m`
}
