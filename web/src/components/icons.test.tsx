import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import * as icons from './icons'

const sources = import.meta.glob('../**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true }) as Record<string, string>

describe('icons', () => {
  it.each(Object.entries(icons).filter(([, v]) => typeof v === 'function'))('%s renders a Tabler svg with a 1.5 stroke', (_name, Icon) => {
    const C = Icon as icons.AppIcon
    const { container } = render(<C aria-hidden />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('class')).toContain('tabler-icon')
    expect(svg.getAttribute('stroke-width')).toBe('1.5')
    expect(svg.getAttribute('aria-hidden')).toBe('true')
  })

  it('lets a caller override size and stroke', () => {
    const { container } = render(<icons.Radar size={28} stroke={2} />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('width')).toBe('28')
    expect(svg.getAttribute('stroke-width')).toBe('2')
  })

  it('is the only icon source: nothing imports lucide-react', () => {
    const offenders = Object.entries(sources)
      .filter(([, text]) => /from ['"]lucide-react['"]/.test(text))
      .map(([path]) => path)
    expect(offenders).toEqual([])
  })
})
