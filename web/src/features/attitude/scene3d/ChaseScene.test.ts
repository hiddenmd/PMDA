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

describe('createChaseScene model load failure', () => {
  it('releases the GL context and returns null', async () => {
    vi.resetModules()
    const forceContextLoss = vi.fn()
    const dispose = vi.fn()
    vi.doMock('three', async (orig) => ({
      ...(await orig<typeof import('three')>()),
      WebGLRenderer: class {
        forceContextLoss = forceContextLoss
        dispose = dispose
      },
    }))
    vi.doMock('three/examples/jsm/loaders/GLTFLoader.js', () => ({
      GLTFLoader: class {
        loadAsync() {
          return Promise.reject(new Error('404'))
        }
      },
    }))
    vi.stubGlobal('WebGL2RenderingContext', class {})
    try {
      const { createChaseScene: create } = await import('./ChaseScene')
      expect(await create(document.createElement('canvas'), 'Helicopter', false)).toBeNull()
      expect(forceContextLoss).toHaveBeenCalledTimes(1)
      expect(dispose).toHaveBeenCalledTimes(1)
    } finally {
      vi.unstubAllGlobals()
      vi.doUnmock('three')
      vi.doUnmock('three/examples/jsm/loaders/GLTFLoader.js')
    }
  })
})
