import '@fontsource/ibm-plex-mono/400.css'
import '@fontsource/ibm-plex-mono/600.css'
import '@fontsource/ibm-plex-sans/400.css'
import '@fontsource/ibm-plex-sans/500.css'
import '@fontsource/ibm-plex-sans/600.css'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { ImportHubProvider } from './hooks/ImportHubProvider'
import './index.css'
import { applyTheme, applyUiSize, initialTheme, initialUiSize } from './lib/preferences'

// Before the first render, so the page never flashes the wrong theme or size.
applyTheme(initialTheme())
applyUiSize(initialUiSize())

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ImportHubProvider>
        <App />
      </ImportHubProvider>
    </QueryClientProvider>
  </StrictMode>,
)
