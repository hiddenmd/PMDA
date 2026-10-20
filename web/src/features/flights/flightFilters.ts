import type { FlightListParams } from '../../api/flights'
import type { FlightSort, FlightStatus } from '../../api/types'

export interface FlightFilters {
  tail: string
  mission: string
  customerId: string
  status: FlightStatus | ''
  /** yyyy-mm-dd (UTC day) */
  startFrom: string
  /** yyyy-mm-dd (UTC day, inclusive) */
  startTo: string
  problems: 'any' | 'with' | 'without'
  /** One database's flights only; empty for all of them. */
  databaseId: string
  sort: FlightSort
}

export const EMPTY_FILTERS: FlightFilters = {
  tail: '',
  mission: '',
  customerId: '',
  status: '',
  startFrom: '',
  startTo: '',
  problems: 'any',
  databaseId: '',
  sort: 'ImportedNewest',
}

export const SORT_LABELS: Record<FlightSort, string> = {
  ImportedNewest: 'Recently imported',
  StartNewest: 'Flight date, newest',
  StartOldest: 'Flight date, oldest',
  Tail: 'Tail',
  MostEvents: 'Most events',
}

const PREFIX = 'f.'
const KEYS = Object.keys(EMPTY_FILTERS) as (keyof FlightFilters)[]

export function filtersFromUrl(search: string): FlightFilters {
  const params = new URLSearchParams(search)
  const filters = { ...EMPTY_FILTERS }
  for (const key of KEYS) {
    const value = params.get(PREFIX + key)
    if (value !== null) (filters as Record<string, string>)[key] = value
  }
  if (!(filters.sort in SORT_LABELS)) filters.sort = EMPTY_FILTERS.sort
  if (!['any', 'with', 'without'].includes(filters.problems)) filters.problems = 'any'
  return filters
}

/** Writes non-default filters into the URL, keeping other parameters (like the selected flight). */
export function filtersToUrl(filters: FlightFilters, href: string): URL {
  const url = new URL(href)
  for (const key of KEYS) {
    const value = filters[key]
    if (value === EMPTY_FILTERS[key]) url.searchParams.delete(PREFIX + key)
    else url.searchParams.set(PREFIX + key, value)
  }
  return url
}

export function activeFilterCount(f: FlightFilters): number {
  return KEYS.filter((k) => k !== 'sort' && f[k] !== EMPTY_FILTERS[k]).length
}

/** The API query for a page of flights. Dates are whole UTC days; "to" includes its day. */
export function toQuery(f: FlightFilters, page: number, pageSize: number): FlightListParams {
  const nextDay = (day: string) => new Date(Date.parse(`${day}T00:00:00Z`) + 86_400_000).toISOString()
  return {
    page,
    pageSize,
    tail: f.tail.trim() || undefined,
    mission: f.mission.trim() || undefined,
    customerId: f.customerId || undefined,
    status: f.status || undefined,
    startFrom: f.startFrom ? `${f.startFrom}T00:00:00Z` : undefined,
    startTo: f.startTo ? nextDay(f.startTo) : undefined,
    hasProblems: f.problems === 'any' ? undefined : f.problems === 'with',
    databaseId: f.databaseId || undefined,
    sort: f.sort,
  }
}
