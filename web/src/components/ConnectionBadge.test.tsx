import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ConnectionBadge } from './ConnectionBadge'

describe('ConnectionBadge', () => {
  it.each([
    ['connected', 'Live'],
    ['connecting', 'Connecting…'],
    ['reconnecting', 'Reconnecting…'],
    ['offline', 'Offline — retrying'],
  ] as const)('%s shows its label and a three-bar link indicator', (status, label) => {
    const { container } = render(<ConnectionBadge status={status} />)
    expect(screen.getByRole('status')).toHaveTextContent(label)
    const bars = container.querySelector('.badge__bars')!
    expect(bars.getAttribute('aria-hidden')).toBe('true')
    expect(bars.querySelectorAll('i')).toHaveLength(3)
  })
})
