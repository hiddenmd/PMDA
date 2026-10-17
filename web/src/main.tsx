import '@fontsource/fira-code/400.css'
import '@fontsource/fira-code/600.css'
import '@fontsource/fira-sans/400.css'
import '@fontsource/fira-sans/500.css'
import '@fontsource/fira-sans/600.css'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { applyTheme, initialTheme } from './lib/theme'
import { ImportHubProvider } from './hooks/ImportHubProvider'
import './index.css'

// Before the first render, so the page never flashes the wrong theme.
applyTheme(initialTheme())

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
