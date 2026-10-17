import { describe, expect, it } from 'vitest'
import { activeFilterCount, EMPTY_FILTERS, filtersFromUrl, filtersToUrl, toQuery } from './flightFilters'

describe('flight filters', () => {
  it('round-trip through the URL and keep other parameters', () => {
    const filters = { ...EMPTY_FILTERS, tail: 'ALPHA', problems: 'with' as const, sort: 'Tail' as const }
    const url = filtersToUrl(filters, 'http://x/?flight=abc&f.mission=old')

    expect(url.searchParams.get('flight')).toBe('abc')
    expect(url.searchParams.get('f.tail')).toBe('ALPHA')
    expect(url.searchParams.has('f.mission')).toBe(false) // back to default
    expect(filtersFromUrl(url.search)).toEqual(filters)
  })

  it('ignores unknown values from a hand-edited URL', () => {
    expect(filtersFromUrl('?f.sort=Bogus&f.problems=maybe')).toMatchObject({ sort: 'ImportedNewest', problems: 'any' })
  })

  it('counts active filters, not the sort order', () => {
    expect(activeFilterCount({ ...EMPTY_FILTERS, sort: 'Tail' })).toBe(0)
    expect(activeFilterCount({ ...EMPTY_FILTERS, tail: 'A', status: 'Failed' })).toBe(2)
  })

  it('builds the API query with whole UTC days, "to" inclusive', () => {
    const q = toQuery({ ...EMPTY_FILTERS, tail: '  A ', startFrom: '2026-03-14', startTo: '2026-03-15', problems: 'without' }, 2, 30)

    expect(q).toMatchObject({
      page: 2,
      pageSize: 30,
      tail: 'A',
      mission: undefined,
      startFrom: '2026-03-14T00:00:00Z',
      startTo: '2026-03-16T00:00:00.000Z',
      hasProblems: false,
    })
  })
})
