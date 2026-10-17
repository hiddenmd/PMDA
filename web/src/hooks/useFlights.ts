import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { getFlight, getFlightDiagnostics, listCustomers, listFlights, type FlightListParams } from '../api/flights'
import { isFinalImportState } from '../api/types'
import { useImportHub } from './useImportHub'

export const flightKeys = {
  all: ['flights'] as const,
  list: (params: FlightListParams) => ['flights', 'list', params] as const,
  detail: (id: string) => ['flights', 'detail', id] as const,
  diagnostics: (id: string) => ['flights', 'diagnostics', id] as const,
}

export function useCustomers() {
  return useQuery({ queryKey: ['flights', 'customers'], queryFn: ({ signal }) => listCustomers(signal), staleTime: 60_000 })
}

export function useFlights(params: FlightListParams) {
  return useQuery({
    queryKey: flightKeys.list(params),
    queryFn: ({ signal }) => listFlights(params, signal),
    placeholderData: keepPreviousData, // keep the current page visible while the next one loads
  })
}

export function useFlight(id: string | null) {
  return useQuery({
    queryKey: flightKeys.detail(id ?? ''),
    queryFn: ({ signal }) => getFlight(id!, signal),
    enabled: id !== null,
  })
}

export function useFlightDiagnostics(id: string | null) {
  return useQuery({
    queryKey: flightKeys.diagnostics(id ?? ''),
    queryFn: ({ signal }) => getFlightDiagnostics(id!, signal),
    enabled: id !== null,
  })
}

/**
 * Refreshes flight data whenever any import (from any user) finishes with a flight, and after the
 * hub reconnects, since updates sent while disconnected are lost.
 */
export function useLiveFlightRefresh() {
  const { hub, status } = useImportHub()
  const queryClient = useQueryClient()
  const wasConnected = useRef(false)

  useEffect(() => {
    if (status === 'connected' && !wasConnected.current) void queryClient.invalidateQueries({ queryKey: flightKeys.all })
    wasConnected.current = status === 'connected'
  }, [status, queryClient])

  useEffect(() => {
    const refresh = () => void queryClient.invalidateQueries({ queryKey: flightKeys.all })
    const offImport = hub.onImportUpdated((job) => {
      if (job.flightId && isFinalImportState(job.state)) refresh()
    })
    const offChanged = hub.onFlightsChanged(refresh)
    return () => {
      offImport()
      offChanged()
    }
  }, [hub, queryClient])
}
