export type Theme = 'dark' | 'light'
export type UiSize = 'compact' | 'default' | 'large'

export const UI_SIZES: { value: UiSize; label: string }[] = [
  { value: 'compact', label: 'Compact' },
  { value: 'default', label: 'Default' },
  { value: 'large', label: 'Large' },
]

const THEME_KEY = 'pmda-theme'
const SIZE_KEY = 'pmda-ui-size'

/** localStorage that never throws (private mode, blocked storage). */
export const storage = {
  get(key: string): string | null {
    try {
      return localStorage.getItem(key)
    } catch {
      return null
    }
  },
  set(key: string, value: string) {
    try {
      localStorage.setItem(key, value)
    } catch {
      // not persisted; still applies for this visit
    }
  },
  remove(key: string) {
    try {
      localStorage.removeItem(key)
    } catch {
      // ignore
    }
  },
}

export function initialTheme(): Theme {
  const saved = storage.get(THEME_KEY)
  return saved === 'light' || saved === 'dark' ? saved : 'dark'
}

export function initialUiSize(): UiSize {
  const saved = storage.get(SIZE_KEY)
  return saved === 'compact' || saved === 'large' || saved === 'default' ? saved : 'default'
}

export function applyTheme(theme: Theme, persist = false) {
  document.documentElement.dataset.theme = theme
  if (persist) storage.set(THEME_KEY, theme)
}

export function applyUiSize(size: UiSize, persist = false) {
  document.documentElement.dataset.uiSize = size
  if (persist) storage.set(SIZE_KEY, size)
}
