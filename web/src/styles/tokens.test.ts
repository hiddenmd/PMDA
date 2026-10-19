import { describe, expect, it } from 'vitest'
import css from './tokens.css?raw'

/** Custom properties declared directly in the first block whose selector is exactly `selector`. */
function block(selector: string): Record<string, string> {
  const start = css.indexOf(`${selector} {`)
  if (start < 0) throw new Error(`no block ${selector}`)
  const body = css.slice(css.indexOf('{', start) + 1, css.indexOf('}', start))
  const out: Record<string, string> = {}
  for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) out[m[1]] = m[2].trim()
  return out
}

function luminance(hex: string): number {
  const h = hex.replace('#', '')
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(h.slice(i, i + 2), 16) / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

const TEXT = ['--color-foreground', '--color-muted-foreground', '--color-primary', '--color-accent', '--color-destructive', '--color-success', '--color-alert-warning', '--color-alert-critical']
const SURFACES = ['--color-background', '--color-card', '--color-panel-header']
const FILLS: [text: string, fill: string][] = [
  ['--color-on-primary', '--color-primary'],
  ['--color-on-accent', '--color-accent'],
  ['--color-on-destructive', '--color-destructive-solid'],
]

describe.each([
  ['dark', ':root'],
  ['light', ":root[data-theme='light']"],
])('%s theme tokens', (_name, selector) => {
  const t = block(selector)

  it.each(TEXT.flatMap((fg) => SURFACES.map((bg) => [fg, bg])))('%s on %s is at least 4.5:1', (fg, bg) => {
    expect(t[fg], fg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(t[bg], bg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(contrast(t[fg], t[bg])).toBeGreaterThanOrEqual(4.5)
  })

  it.each(FILLS)('%s on %s is at least 4.5:1', (fg, bg) => {
    expect(contrast(t[fg], t[bg])).toBeGreaterThanOrEqual(4.5)
  })
})
