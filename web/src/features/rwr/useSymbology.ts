import { useQuery } from '@tanstack/react-query'
import { getProfileSeries } from '../../api/dataViews'
import { getCustomerSymbology, getSymbology, listSymbology } from '../../api/symbology'
import { useWorkspace } from '../../workspace/workspaceContext'
import { resolveProfile } from './symbology'

/**
 * The symbology for the selected flight: the analyst's explicit choice, or else the profile of the
 * flight's customer (the server falls back to the default profile).
 */
export function useSymbology() {
  const { flight, symbologyId, setSymbologyId } = useWorkspace()
  const profiles = useQuery({ queryKey: ['symbology', 'list'], queryFn: ({ signal }) => listSymbology(signal), staleTime: Infinity })
  const profile = useQuery({
    queryKey: ['symbology', symbologyId ? `id:${symbologyId}` : `customer:${flight?.customerId}`],
    queryFn: ({ signal }) => (symbologyId ? getSymbology(symbologyId, signal) : getCustomerSymbology(flight!.customerId, signal)),
    enabled: symbologyId !== null || flight !== undefined,
    select: resolveProfile,
    staleTime: Infinity,
  })
  return { profiles: profiles.data ?? [], profile: profile.data, choice: symbologyId, choose: setSymbologyId, error: profile.error }
}

/** The flight's countermeasure releases (for the remaining-countermeasures counter). */
export function useReleases(flightId: string) {
  return useQuery({
    queryKey: ['flights', 'releases', flightId],
    queryFn: ({ signal }) => getProfileSeries(flightId, { buckets: 1 }, signal),
    select: (s) => s.countermeasures,
    staleTime: Infinity, // a completed flight never changes
  })
}
