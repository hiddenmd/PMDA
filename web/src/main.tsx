import '@fontsource/ibm-plex-mono/400.css'
import '@fontsource/ibm-plex-mono/600.css'
import '@fontsource/ibm-plex-sans/400.css'
import '@fontsource/ibm-plex-sans/500.css'
import '@fontsource/ibm-plex-sans/600.css'
import '@fontsource/saira-condensed/600.css'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ApiError } from './api/client'
import { App } from './App'
import { AuthGate } from './auth/AuthGate'
import { authKeys } from './auth/authContext'
import { ImportHubProvider } from './hooks/ImportHubProvider'
import './index.css'
import { applyTheme, applyUiSize, initialTheme, initialUiSize } from './lib/preferences'

// Before the first render, so the page never flashes the wrong theme or size.
applyTheme(initialTheme())
applyUiSize(initialUiSize())

// A 401 anywhere means the session ended: clear it and the gate shows the login screen.
const onError = (error: unknown) => {
  if (error instanceof ApiError && error.status === 401) queryClient.setQueryData(authKeys.me, null)
}
const queryClient: QueryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthGate>
        <ImportHubProvider>
          <App />
        </ImportHubProvider>
      </AuthGate>
    </QueryClientProvider>
  </StrictMode>,
)
