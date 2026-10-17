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

const numberFormat = new Intl.NumberFormat()
export const formatNumber = (n: number) => numberFormat.format(n)

const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'medium',
  timeZone: 'UTC',
})

/** Flight times are shown in UTC, as recorded. */
export const formatUtc = (iso: string | null | undefined) => (iso ? `${dateTimeFormat.format(new Date(iso))} UTC` : '—')
