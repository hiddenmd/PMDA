import { useQuery } from '@tanstack/react-query'
import { databaseKeys, getDatabaseChoices } from '../api/databases'

/** Enabled flight databases (to filter by, and to import into unless read-only) and the import default. */
export function useDatabaseChoices() {
  return useQuery({ queryKey: databaseKeys.choices, queryFn: ({ signal }) => getDatabaseChoices(signal), staleTime: 60_000 })
}
