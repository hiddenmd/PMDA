import * as THREE from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import type { CustomLayerInterface, CustomRenderMethodInput, Map as MlMap } from 'maplibre-gl'
import { MercatorCoordinate } from 'maplibre-gl'
import type { FlightTrack } from '../../../api/types'
import { attitudeAt, deriveAttitude } from '../../attitude/attitude'
import { attitudeQuaternion, toLocal } from '../../attitude/scene3d/frame'
import { positionAt } from '../track'

const DROP_EVERY_MS = 30_000
const MODEL_SCALE = 40
const DOME_OPACITY = 0.12
const DOME_LOCKED_OPACITY = 0.3
const MODEL_URL = { FixedWing: '/models/jet.glb', Helicopter: '/models/helicopter.glb' } as const

export const localFrame = (origin: { lat: number; lon: number }, p: { lat: number; lon: number; altM: number }) =>
  toLocal({ lat: origin.lat, lon: origin.lon, altM: 0 }, p)

export function dropLineTimes(t: readonly number[]): number[] {
  if (t.length === 0) return []
  const out: number[] = []
  for (let ms = Math.ceil(t[0] / DROP_EVERY_MS) * DROP_EVERY_MS; ms <= t[t.length - 1]; ms += DROP_EVERY_MS) out.push(ms)
  return out
}

export interface SiteDome {
  emitterId: number
  lat: number
  lon: number
  range: number
  color: string
}

export interface FlightLayer extends CustomLayerInterface {
  setPlayhead(ms: number, lockedEmitterIds: ReadonlySet<number>): void
  setGroundElevation(fn: (lon: number, lat: number) => number | null): void
}

/**
 * The flight in 3D over MapLibre terrain: the track as a ribbon at true MSL altitude (whole dim, flown bright) with
 * drop lines to the ground, the aircraft model oriented from the derived attitude, and translucent domes over
 * ESTIMATED sites (nominal range). One three.js scene in local metres (x east, y up, z south) around the track's
 * first point, mapped into MapLibre's mercator space by the layer matrix.
 */
export function createFlightLayer(opts: { track: FlightTrack; fixes: SiteDome[]; platform: 'FixedWing' | 'Helicopter'; color: string; dimColor: string }): FlightLayer {
  const { track } = opts
  const origin = { lat: track.lat[0], lon: track.lon[0] }
  const originMerc = MercatorCoordinate.fromLngLat([origin.lon, origin.lat], 0)
  const s = originMerc.meterInMercatorCoordinateUnits()
  // (x, y, z) local → (x, z, y) mercator, scaled, then translated: a reflection, hence DoubleSide materials.
  const toMercator = new THREE.Matrix4()
    .makeTranslation(originMerc.x, originMerc.y, originMerc.z)
    .multiply(new THREE.Matrix4().makeScale(s, s, s))
    .multiply(new THREE.Matrix4().set(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1))

  const scene = new THREE.Scene()
  const camera = new THREE.Camera()
  let renderer: THREE.WebGLRenderer | null = null
  let map: MlMap | null = null
  let ground: (lon: number, lat: number) => number | null = () => null

  scene.add(new THREE.HemisphereLight(0xb0c8e8, 0x202830, 1.2))
  const sun = new THREE.DirectionalLight(0xffffff, 1.2)
  sun.position.set(1, 2, 1)
  scene.add(sun)

  const local = track.t.map((_, i) => localFrame(origin, { lat: track.lat[i], lon: track.lon[i], altM: track.altM[i] }))
  const whole = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints(local.map(([x, y, z]) => new THREE.Vector3(x, y, z))),
    new THREE.LineBasicMaterial({ color: opts.dimColor, transparent: true, opacity: 0.6 }),
  )
  whole.frustumCulled = false
  scene.add(whole)
  const flownGeometry = new THREE.BufferGeometry()
  flownGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(local.length * 3 + 3), 3))
  flownGeometry.setDrawRange(0, 0)
  const flown = new THREE.Line(flownGeometry, new THREE.LineBasicMaterial({ color: opts.color }))
  flown.frustumCulled = false
  scene.add(flown)

  const dropTimes = dropLineTimes(track.t)
  const dropGeometry = new THREE.BufferGeometry()
  dropGeometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(dropTimes.length * 6), 3))
  const drops = new THREE.LineSegments(dropGeometry, new THREE.LineBasicMaterial({ color: opts.dimColor, transparent: true, opacity: 0.5 }))
  drops.frustumCulled = false
  scene.add(drops)

  const domes = opts.fixes.map((f) => {
    const [x, , z] = localFrame(origin, { lat: f.lat, lon: f.lon, altM: 0 })
    const mesh = new THREE.Mesh(
      new THREE.SphereGeometry(f.range, 48, 16, 0, Math.PI * 2, 0, Math.PI / 2),
      new THREE.MeshBasicMaterial({ color: f.color, transparent: true, opacity: DOME_OPACITY, depthWrite: false, side: THREE.DoubleSide }),
    )
    mesh.position.set(x, 0, z)
    mesh.frustumCulled = false
    mesh.userData = { emitterId: f.emitterId, lat: f.lat, lon: f.lon }
    scene.add(mesh)
    return mesh
  })

  const aircraft = new THREE.Group()
  aircraft.scale.setScalar(MODEL_SCALE)
  scene.add(aircraft)
  void new GLTFLoader()
    .loadAsync(MODEL_URL[opts.platform])
    .then((gltf) => {
      gltf.scene.traverse((o) => {
        if (o instanceof THREE.Mesh) {
          o.material = new THREE.MeshStandardMaterial({ color: 0x8b97a8, metalness: 0.55, roughness: 0.45, side: THREE.DoubleSide })
          o.frustumCulled = false
        }
      })
      aircraft.add(gltf.scene)
      map?.triggerRepaint()
    })
    .catch((error: unknown) => console.warn('Aircraft model failed to load', error))

  const series = deriveAttitude(track)

  /** Stands the drop lines and domes on the ground; true when any height changed. */
  function placeDrops(): boolean {
    let changed = false
    const pos = dropGeometry.getAttribute('position') as THREE.BufferAttribute
    dropTimes.forEach((ms, i) => {
      const p = positionAt(track, ms)
      if (!p) return
      const [x, y, z] = localFrame(origin, p)
      const g = ground(p.lon, p.lat) ?? 0
      if (pos.getY(i * 2) !== y || pos.getY(i * 2 + 1) !== g) changed = true
      pos.setXYZ(i * 2, x, y, z)
      pos.setXYZ(i * 2 + 1, x, g, z)
    })
    if (changed) pos.needsUpdate = true
    for (const d of domes) {
      const g = ground(d.userData.lon, d.userData.lat) ?? 0
      if (d.position.y !== g) changed = true
      d.position.y = g
    }
    return changed
  }

  return {
    id: 'flight-3d',
    type: 'custom',
    renderingMode: '3d',
    onAdd(m, gl) {
      map = m
      renderer = new THREE.WebGLRenderer({ canvas: m.getCanvas(), context: gl, antialias: true })
      renderer.autoClear = false
      placeDrops()
    },
    render(_gl, args: CustomRenderMethodInput) {
      camera.projectionMatrix = new THREE.Matrix4().fromArray(args.defaultProjectionData.mainMatrix as unknown as number[]).multiply(toMercator)
      renderer!.resetState()
      renderer!.render(scene, camera)
    },
    onRemove() {
      scene.traverse((o) => {
        if (o instanceof THREE.Mesh || o instanceof THREE.Line) {
          o.geometry.dispose()
          ;(Array.isArray(o.material) ? o.material : [o.material]).forEach((mat) => mat.dispose())
        }
      })
      renderer = null // shares MapLibre's context: never dispose or lose it here
      map = null
    },
    setPlayhead(ms, locked) {
      const p = positionAt(track, ms)
      const a = attitudeAt(series, ms)
      if (p) {
        const [x, y, z] = localFrame(origin, p)
        aircraft.position.set(x, y, z)
        // Flown ribbon up to the playhead.
        const pos = flownGeometry.getAttribute('position') as THREE.BufferAttribute
        let n = 0
        for (; n < track.t.length && track.t[n] <= ms; n++) pos.setXYZ(n, ...local[n])
        pos.setXYZ(n, x, y, z)
        pos.needsUpdate = true
        flownGeometry.setDrawRange(0, n + 1)
      }
      if (a) aircraft.quaternion.copy(attitudeQuaternion(a.pitch, a.roll, a.heading))
      for (const d of domes) (d.material as THREE.MeshBasicMaterial).opacity = locked.has(d.userData.emitterId) ? DOME_LOCKED_OPACITY : DOME_OPACITY
      map?.triggerRepaint()
    },
    setGroundElevation(fn) {
      ground = fn
      if (placeDrops()) map?.triggerRepaint()
    },
  }
}
