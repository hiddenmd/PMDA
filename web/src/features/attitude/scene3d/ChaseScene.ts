import * as THREE from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import type { FlightTrack } from '../../../api/types'
import type { Attitude } from '../attitude'
import { positionAt, type TrackPoint } from '../../map/track'
import { attitudeQuaternion, cameraOffset, damp, orbitPitchRange, toLocal, trailPoints, type CameraPreset } from './frame'

export interface SceneColors {
  background: string
  grid: string
  gridMinor: string
  accent: string
}
export interface ChaseFrame {
  attitude: Attitude
  track: FlightTrack
  ms: number
}
export interface ChaseScene {
  setFrame(frame: ChaseFrame): void
  setPreset(preset: CameraPreset): void
  orbitBy(dYawDeg: number, dPitchDeg: number): void
  resetView(): void
  setColors(colors: SceneColors): void
  resize(width: number, height: number, pixelRatio: number): void
  /** Draws one frame; returns true while it needs another (damping, rotors). */
  render(nowMs: number): boolean
  dispose(): void
}

const MODEL_URL = { FixedWing: '/models/jet.glb', Helicopter: '/models/helicopter.glb' } as const
const TRAIL_MAX = 240
const MAIN_REV_S = 6
const TAIL_REV_S = 30
const DAMPING_BASE = 0.05
const GRID_SIZE = 600
const GRID_CELL = 10
const GRID_Y = -6
const GRID_STEP_MAX_M = 5000

/** A chase view of the aircraft at the playhead; null where WebGL is unavailable (the caller falls back to 2D). */
export async function createChaseScene(
  canvas: HTMLCanvasElement,
  platform: 'FixedWing' | 'Helicopter',
  reducedMotion: boolean,
): Promise<ChaseScene | null> {
  if (typeof WebGL2RenderingContext === 'undefined') return null
  let renderer: THREE.WebGLRenderer
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false })
  } catch {
    return null
  }
  try {
    const gltf = await new GLTFLoader().loadAsync(MODEL_URL[platform])
    return new Scene(renderer, gltf.scene, reducedMotion)
  } catch {
    // Free the GL context so the caller's 2D fallback does not leak one per failed mount.
    renderer.forceContextLoss()
    renderer.dispose()
    return null
  }
}

class Scene implements ChaseScene {
  private scene = new THREE.Scene()
  private camera = new THREE.PerspectiveCamera(42, 1, 0.1, 2000)
  private aircraft = new THREE.Group()
  private body = new THREE.MeshStandardMaterial({ color: 0x8b97a8, metalness: 0.55, roughness: 0.45 })
  private glass = new THREE.MeshStandardMaterial({ color: 0x1b2a40, metalness: 0.2, roughness: 0.1 })
  private edges = new THREE.LineBasicMaterial({ transparent: true, opacity: 0.75 })
  private rim = new THREE.DirectionalLight(0xffffff, 2.2)
  private grid: THREE.GridHelper | null = null
  private trailGeometry = new THREE.BufferGeometry()
  private trailMaterial = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0.9, fog: false })
  private mainRotor: THREE.Object3D | null
  private tailRotor: THREE.Object3D | null
  private preset: CameraPreset = 'chase'
  private orbit = { yaw: 0, pitch: 0 }
  private heading = 0
  private camOffset: THREE.Vector3 | null = null
  private colors: SceneColors = { background: '#080b12', grid: '#22324d', gridMinor: '#1a2436', accent: '#4cc3ff' }
  private gridOffset = { x: 0, z: 0 }
  private prevPosition: TrackPoint | null = null
  private lastNow = 0
  private dirty = true

  private renderer: THREE.WebGLRenderer
  private reducedMotion: boolean

  constructor(renderer: THREE.WebGLRenderer, model: THREE.Object3D, reducedMotion: boolean) {
    this.renderer = renderer
    this.reducedMotion = reducedMotion
    this.scene.add(new THREE.HemisphereLight(0x6b8fbf, 0x080b12, 0.9))
    const key = new THREE.DirectionalLight(0xffffff, 0.9)
    key.position.set(8, 12, -6)
    this.scene.add(key)
    this.rim.position.set(-10, 6, 14)
    this.scene.add(this.rim)

    // Collect first, then add the edge lines: adding children during traverse would revisit them.
    const meshes: THREE.Mesh[] = []
    model.traverse((o) => {
      if (o instanceof THREE.Mesh) meshes.push(o)
    })
    for (const o of meshes) {
      const isCanopy = o.name === 'Canopy' || o.parent?.name === 'Canopy'
      o.material = isCanopy ? this.glass : this.body
      if (!isCanopy) o.add(new THREE.LineSegments(new THREE.EdgesGeometry(o.geometry, 25), this.edges))
    }
    this.mainRotor = model.getObjectByName('MainRotor') ?? null
    this.tailRotor = model.getObjectByName('TailRotor') ?? null
    this.aircraft.add(model)
    this.scene.add(this.aircraft)

    this.trailGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(TRAIL_MAX * 3), 3))
    this.trailGeometry.setAttribute('color', new THREE.BufferAttribute(new Float32Array(TRAIL_MAX * 3), 3))
    this.trailGeometry.setDrawRange(0, 0)
    const trail = new THREE.Line(this.trailGeometry, this.trailMaterial)
    trail.frustumCulled = false // the buffer's bounding sphere is never recomputed
    this.scene.add(trail)
    this.applyColors()
  }

  setFrame({ attitude, track, ms }: ChaseFrame) {
    this.aircraft.quaternion.copy(attitudeQuaternion(attitude.pitch, attitude.roll, attitude.heading))
    this.heading = attitude.heading
    // The aircraft stays at the origin; the trail and grid move around it (no float drift on long flights).
    // The grid shifts by the distance flown modulo one cell, so the ground visibly passes underneath.
    // Incremental, so the east-west scale stays local on long flights; a seek (big step) keeps the phase.
    const here = positionAt(track, ms)
    if (here) {
      if (this.prevPosition) {
        const [x, , z] = toLocal(this.prevPosition, here)
        if (Math.hypot(x, z) < GRID_STEP_MAX_M) {
          const wrap = (v: number) => ((v % GRID_CELL) + GRID_CELL) % GRID_CELL
          this.gridOffset = { x: wrap(this.gridOffset.x + x), z: wrap(this.gridOffset.z + z) }
        }
      }
      this.prevPosition = here
      if (this.grid) this.grid.position.set(-this.gridOffset.x, GRID_Y, -this.gridOffset.z)
    }
    const pts = trailPoints(track, ms, 60_000, TRAIL_MAX)
    const pos = this.trailGeometry.getAttribute('position') as THREE.BufferAttribute
    pos.array.set(pts)
    pos.needsUpdate = true
    const n = pts.length / 3
    this.trailGeometry.setDrawRange(0, n)
    this.paintTrail(n)
    this.dirty = true
  }

  setPreset(preset: CameraPreset) {
    this.preset = preset
    this.orbit = { yaw: 0, pitch: 0 }
    this.dirty = true
  }

  orbitBy(dYawDeg: number, dPitchDeg: number) {
    const [lo, hi] = orbitPitchRange(this.preset)
    this.orbit = { yaw: this.orbit.yaw + dYawDeg, pitch: Math.max(lo, Math.min(hi, this.orbit.pitch + dPitchDeg)) }
    this.dirty = true
  }

  resetView() {
    this.setPreset(this.preset)
  }

  setColors(colors: SceneColors) {
    this.colors = colors
    this.applyColors()
    this.dirty = true
  }

  resize(width: number, height: number, pixelRatio: number) {
    this.renderer.setPixelRatio(pixelRatio)
    this.renderer.setSize(width, height, false)
    this.camera.aspect = width / Math.max(1, height)
    this.camera.updateProjectionMatrix()
    this.dirty = true
  }

  render(nowMs: number): boolean {
    const dt = this.lastNow ? Math.min(0.1, (nowMs - this.lastNow) / 1000) : 0
    this.lastNow = nowMs
    const spinning = this.mainRotor !== null
    if (spinning) {
      const k = this.reducedMotion ? 0.25 : 1
      this.mainRotor!.rotation.y -= MAIN_REV_S * 2 * Math.PI * dt * k
      if (this.tailRotor) this.tailRotor.rotation.x -= TAIL_REV_S * 2 * Math.PI * dt * k
    }
    const want = cameraOffset(this.preset, this.heading, this.orbit)
    if (!this.camOffset || this.reducedMotion) this.camOffset = want.clone()
    else this.camOffset.lerp(want, damp(DAMPING_BASE, dt))
    const settling = this.camOffset.distanceToSquared(want) > 1e-4
    if (!this.dirty && !settling && !spinning) return false
    this.camera.up.set(0, 1, 0)
    this.camera.position.copy(this.camOffset)
    this.camera.lookAt(0, 0.5, 0)
    this.renderer.render(this.scene, this.camera)
    this.dirty = false
    return settling || spinning
  }

  dispose() {
    this.scene.traverse((o) => {
      if (o instanceof THREE.Mesh || o instanceof THREE.LineSegments || o instanceof THREE.Line) o.geometry.dispose()
    })
    for (const m of [this.body, this.glass, this.edges, this.trailMaterial]) m.dispose()
    this.grid?.dispose()
    this.renderer.dispose()
    this.renderer.forceContextLoss()
  }

  private applyColors() {
    const bg = new THREE.Color(this.colors.background)
    this.scene.background = bg
    this.scene.fog = new THREE.Fog(bg, 70, 320)
    this.edges.color.set(this.colors.accent)
    this.rim.color.set(this.colors.accent)
    if (this.grid) {
      this.scene.remove(this.grid)
      this.grid.dispose()
    }
    this.grid = new THREE.GridHelper(GRID_SIZE, GRID_SIZE / GRID_CELL, this.colors.gridMinor, this.colors.gridMinor)
    this.grid.position.set(-this.gridOffset.x, GRID_Y, -this.gridOffset.z)
    this.scene.add(this.grid)
    this.paintTrail(this.trailGeometry.drawRange.count)
  }

  /** Trail colour fades from the background (oldest) to the accent (newest). */
  private paintTrail(n: number) {
    const col = this.trailGeometry.getAttribute('color') as THREE.BufferAttribute
    const from = new THREE.Color(this.colors.background)
    const to = new THREE.Color(this.colors.accent)
    for (let i = 0; i < n; i++) {
      const c = from.clone().lerp(to, n > 1 ? i / (n - 1) : 1)
      col.setXYZ(i, c.r, c.g, c.b)
    }
    col.needsUpdate = true
  }
}
