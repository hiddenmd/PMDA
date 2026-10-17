import { Moon, Sun } from 'lucide-react'
import { useState } from 'react'
import { applyTheme, saveTheme, type Theme } from '../lib/theme'

export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(() => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark'))
  const next: Theme = theme === 'dark' ? 'light' : 'dark'

  const toggle = () => {
    applyTheme(next)
    saveTheme(next)
    setTheme(next)
  }

  return (
    <button type="button" className="btn btn--ghost btn--icon" aria-label={`Switch to ${next} theme`} title={`Switch to ${next} theme`} onClick={toggle}>
      {theme === 'dark' ? <Sun size={18} aria-hidden /> : <Moon size={18} aria-hidden />}
    </button>
  )
}
