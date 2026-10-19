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

function rgb(hex: string): [number, number, number] {
  const h = hex.replace('#', '')
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number]
}

/** Composite a `rgb(r g b / a)` colour over an opaque hex base; returns hex. */
function composite(overlay: string, base: string): string {
  const m = overlay.match(/^rgb\(\s*(\d+)\s+(\d+)\s+(\d+)\s*\/\s*([\d.]+)\s*\)$/)
  if (!m) throw new Error(`unparseable colour ${overlay}`)
  const a = Number(m[4])
  const b = rgb(base)
  const out = [1, 2, 3].map((i, k) => Math.round(Number(m[i]) * a + b[k] * (1 - a)))
  return '#' + out.map((v) => v.toString(16).padStart(2, '0')).join('')
}

function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x)
  return (hi + 0.05) / (lo + 0.05)
}

const TEXT = ['--color-foreground', '--color-muted-foreground', '--color-primary', '--color-accent', '--color-destructive', '--color-success', '--color-alert-warning', '--color-alert-critical']
const SURFACES = ['--color-background', '--color-card', '--color-panel-header', '--color-muted', '--color-hover']
// Cyan (--color-primary) text never sits on the selection fill, so it is not in this list.
const SELECTION_TEXT = ['--color-foreground', '--color-muted-foreground', '--color-accent', '--color-destructive', '--color-success', '--color-alert-warning', '--color-alert-critical']
const SELECTION_BASES = ['--color-background', '--color-card', '--color-panel-header', '--color-muted']
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

  it.each(SELECTION_TEXT.flatMap((fg) => SELECTION_BASES.map((bg) => [fg, bg])))('%s on selection over %s is at least 4.5:1', (fg, bg) => {
    expect(t[fg], fg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(t[bg], bg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(contrast(t[fg], composite(t['--color-selection'], t[bg]))).toBeGreaterThanOrEqual(4.5)
  })

  it.each(FILLS)('%s on %s is at least 4.5:1', (fg, bg) => {
    expect(t[fg], fg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(t[bg], bg).toMatch(/^#[0-9a-f]{6}$/i)
    expect(contrast(t[fg], t[bg])).toBeGreaterThanOrEqual(4.5)
  })
})
