export type Theme = 'dark' | 'light'

const STORAGE_KEY = 'pmda-theme'

/** Theme saved earlier, else dark (the default for analysis work). Storage may be unavailable. */
export function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    // private mode or blocked storage: fall through
  }
  return 'dark'
}

export function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme
}

export function saveTheme(theme: Theme) {
  try {
    localStorage.setItem(STORAGE_KEY, theme)
  } catch {
    // not persisted; the theme still applies for this visit
  }
}
