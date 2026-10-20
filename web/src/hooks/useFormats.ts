import { useQuery } from '@tanstack/react-query'
import { formatKeys, listFormats } from '../api/formats'

/** Header-defined recording formats, for the import picker and the Formats panel. */
export function useFormats() {
  return useQuery({ queryKey: formatKeys.list, queryFn: ({ signal }) => listFormats(signal), staleTime: 60_000 })
}
