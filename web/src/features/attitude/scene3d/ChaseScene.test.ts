import { describe, expect, it, vi } from 'vitest'
import { createChaseScene } from './ChaseScene'

describe('createChaseScene', () => {
  it('returns null where WebGL is unavailable, without touching the canvas', async () => {
    const canvas = document.createElement('canvas')
    const getContext = vi.spyOn(canvas, 'getContext')
    expect(await createChaseScene(canvas, 'FixedWing', false)).toBeNull()
    expect(getContext).not.toHaveBeenCalled()
  })
})
