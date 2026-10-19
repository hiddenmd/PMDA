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

describe('Scene (no WebGL)', () => {
  const renderer = {
    render: vi.fn(),
    setSize: vi.fn(),
    setPixelRatio: vi.fn(),
    dispose: vi.fn(),
    forceContextLoss: vi.fn(),
  }
  const track = {
    t: [0, 10_000],
    lat: [45, 45],
    lon: [10, 10 + 1030 / (6_371_000 * (Math.PI / 180) * Math.cos(Math.PI / 4))],
    altM: [1000, 1000],
  } as unknown as import('../../../api/types').FlightTrack
  const attitude = { pitch: 0, roll: 0, heading: 90 } as unknown as import('../attitude').Attitude
  const deg = (r: number) => (r * 180) / Math.PI

  async function setup() {
    vi.resetModules()
    for (const fn of Object.values(renderer)) fn.mockClear()
    vi.doMock('three', async () => {
      const actual = await vi.importActual<typeof import('three')>('three')
      return { ...actual, WebGLRenderer: class { render = renderer.render; setSize = renderer.setSize; setPixelRatio = renderer.setPixelRatio; dispose = renderer.dispose; forceContextLoss = renderer.forceContextLoss } }
    })
    vi.doMock('three/examples/jsm/loaders/GLTFLoader.js', async () => {
      const T = await vi.importActual<typeof import('three')>('three')
      return {
        GLTFLoader: class {
          loadAsync() {
            const group = new T.Group()
            const body = new T.Mesh(new T.BoxGeometry(1, 1, 1))
            body.name = 'Body'
            const canopy = new T.Mesh(new T.BoxGeometry(1, 1, 1))
            canopy.name = 'Canopy'
            group.add(body, canopy)
            return Promise.resolve({ scene: group })
          }
        },
      }
    })
    vi.stubGlobal('WebGL2RenderingContext', class {})
    const { createChaseScene: create } = await import('./ChaseScene')
    const THREE = await vi.importActual<typeof import('three')>('three')
    const scene = (await create(document.createElement('canvas'), 'FixedWing', true))!
    expect(scene).not.toBeNull()
    const lastCall = () => renderer.render.mock.calls.at(-1)! as [import('three').Scene, import('three').PerspectiveCamera]
    return { scene, THREE, lastCall }
  }
  const teardown = () => {
    vi.unstubAllGlobals()
    vi.doUnmock('three')
    vi.doUnmock('three/examples/jsm/loaders/GLTFLoader.js')
  }

  it('dispose releases the context and the geometries and materials it created', async () => {
    const { scene, THREE } = await setup()
    try {
      const geo = vi.spyOn(THREE.BufferGeometry.prototype, 'dispose')
      const mat = vi.spyOn(THREE.Material.prototype, 'dispose')
      scene.dispose()
      expect(renderer.dispose).toHaveBeenCalledTimes(1)
      expect(renderer.forceContextLoss).toHaveBeenCalledTimes(1)
      expect(geo.mock.calls.length).toBeGreaterThanOrEqual(4) // box x2, trail, grid, edges
      expect(mat.mock.calls.length).toBeGreaterThanOrEqual(5) // body, glass, edges, trail, grid
      geo.mockRestore()
      mat.mockRestore()
    } finally {
      teardown()
    }
  })

  it('clamps the Top-view orbit so the camera elevation stays within [-10, 89] degrees', async () => {
    const { scene, lastCall } = await setup()
    try {
      scene.setPreset('top')
      scene.orbitBy(0, 500)
      scene.render(1000)
      let p = lastCall()[1].position
      expect(deg(Math.asin(p.y / p.length()))).toBeLessThanOrEqual(89.0001)
      scene.orbitBy(0, -500)
      scene.render(2000)
      p = lastCall()[1].position
      expect(deg(Math.asin(p.y / p.length()))).toBeGreaterThanOrEqual(-10.0001)
    } finally {
      teardown()
    }
  })

  it('rotates the Top picture when orbiting in yaw', async () => {
    const { scene, THREE, lastCall } = await setup()
    try {
      scene.setPreset('top')
      scene.render(1000)
      const screenUp = () => new THREE.Vector3(0, 1, 0).applyQuaternion(lastCall()[1].quaternion)
      const before = screenUp().clone()
      scene.orbitBy(90, 0)
      scene.render(2000)
      expect(screenUp().distanceTo(before)).toBeGreaterThan(0.5)
    } finally {
      teardown()
    }
  })

  it('drifts the grid along the flight direction only', async () => {
    const { scene, THREE, lastCall } = await setup()
    try {
      const gridPos = () => lastCall()[0].children.find((o) => o instanceof THREE.GridHelper)!.position
      scene.setFrame({ attitude, track, ms: 0 })
      scene.render(1000)
      const a = gridPos().clone()
      scene.setFrame({ attitude, track, ms: 1000 })
      scene.render(2000)
      const b = gridPos()
      expect(Math.abs(b.z - a.z)).toBeLessThan(1e-6)
      expect(Math.abs(b.x - a.x)).toBeGreaterThan(1)
    } finally {
      teardown()
    }
  })
})
