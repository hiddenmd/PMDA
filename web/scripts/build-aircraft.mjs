/**
 * Authors PMDA's aircraft models with three.js geometry and writes them as binary glTF to public/models.
 * Frame: −Z forward (nose), +Y up, +X right wing, metres; origin near the centre of gravity.
 * Materials are placeholders: the app assigns its own by node name (Body, Canopy, MainRotor, TailRotor).
 * Run: npm --prefix web run build:models
 */
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import * as THREE from 'three'
import { GLTFExporter } from 'three/examples/jsm/exporters/GLTFExporter.js'

// GLTFExporter reads Blobs with FileReader, which Node lacks.
globalThis.FileReader ??= class {
  readAsArrayBuffer(blob) {
    blob.arrayBuffer().then((r) => {
      this.result = r
      this.onloadend?.()
    })
  }
  readAsDataURL(blob) {
    blob.arrayBuffer().then((r) => {
      this.result = `data:${blob.type || 'application/octet-stream'};base64,${Buffer.from(r).toString('base64')}`
      this.onloadend?.()
    })
  }
}

const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'models')
const grey = new THREE.MeshStandardMaterial({ color: 0x8b97a8, metalness: 0.55, roughness: 0.45 })
const glass = new THREE.MeshStandardMaterial({ color: 0x1b2a40, metalness: 0.2, roughness: 0.1 })

const mesh = (geometry, name, material = grey) => Object.assign(new THREE.Mesh(geometry, material), { name })

/** Body of revolution along −Z from a profile of [z, radius] pairs (z increasing = aft). */
function lathe(profile, segments = 24) {
  const pts = profile.map(([z, r]) => new THREE.Vector2(Math.max(r, 0.001), z))
  const g = new THREE.LatheGeometry(pts, segments)
  g.rotateX(Math.PI / 2) // lathe axis Y → Z (profile z keeps its sign: −Z forward)
  return g
}

/** Flat plate in the XZ plane from [x, z] points, `thick` metres thick, centred on y. */
function plate(points, thick) {
  const shape = new THREE.Shape(points.map(([x, z]) => new THREE.Vector2(x, z)))
  const g = new THREE.ExtrudeGeometry(shape, { depth: thick, bevelEnabled: true, bevelThickness: thick * 0.3, bevelSize: thick * 0.3, bevelSegments: 1 })
  g.rotateX(Math.PI / 2) // shape y → world z, extrusion → −y
  g.translate(0, thick / 2, 0)
  return g
}

/** Vertical plate in the YZ plane from [z, y] points (a fin), `thick` metres thick. */
function fin(points, thick) {
  const shape = new THREE.Shape(points.map(([z, y]) => new THREE.Vector2(z, y)))
  const g = new THREE.ExtrudeGeometry(shape, { depth: thick, bevelEnabled: false })
  g.rotateY(-Math.PI / 2) // shape x → world z
  g.translate(thick / 2, 0, 0)
  return g
}

const mirrorX = (points) => points.map(([x, z]) => [-x, z])

function jet() {
  const root = new THREE.Group()
  root.name = 'Jet'
  const body = new THREE.Group()
  body.name = 'Body'
  root.add(body)
  // Fuselage: pointed nose at z −8.5, widest at the intakes, tapering to the nozzle at z +7.
  body.add(mesh(lathe([[-8.5, 0], [-7.6, 0.28], [-6, 0.62], [-3.5, 0.9], [0, 1], [4, 0.9], [6.4, 0.68], [7, 0.62]]), 'Fuselage'))
  const nozzle = new THREE.CylinderGeometry(0.58, 0.5, 0.9, 20, 1, true)
  nozzle.rotateX(Math.PI / 2)
  nozzle.translate(0, 0, 7.4)
  body.add(mesh(nozzle, 'Nozzle'))
  for (const side of [1, -1]) {
    const intake = new THREE.BoxGeometry(0.7, 0.9, 4.2)
    intake.translate(side * 1.05, -0.15, -1.6)
    body.add(mesh(intake, side > 0 ? 'IntakeRight' : 'IntakeLeft'))
  }
  const wing = [[0.9, -2.2], [5.1, 2.4], [5.1, 3.4], [0.9, 3.6]]
  body.add(mesh(plate(wing, 0.16), 'WingRight'))
  body.add(mesh(plate(mirrorX(wing), 0.16), 'WingLeft'))
  const tail = [[0.7, 4.6], [2.8, 6.4], [2.8, 7.2], [0.7, 7.3]]
  body.add(mesh(plate(tail, 0.1), 'TailplaneRight'))
  body.add(mesh(plate(mirrorX(tail), 0.1), 'TailplaneLeft'))
  const finGeo = fin([[3.2, 0.6], [6.4, 3.6], [7.3, 3.6], [7.2, 0.6]], 0.14)
  body.add(mesh(finGeo, 'Fin'))
  const canopy = new THREE.SphereGeometry(0.62, 24, 12, 0, Math.PI * 2, 0, Math.PI / 2)
  canopy.scale(1, 0.85, 2.6)
  canopy.translate(0, 0.62, -4.6)
  root.add(mesh(canopy, 'Canopy', glass))
  return root
}

function helicopter() {
  const root = new THREE.Group()
  root.name = 'Helicopter'
  const body = new THREE.Group()
  body.name = 'Body'
  root.add(body)
  body.add(mesh(lathe([[-3.4, 0], [-3.1, 0.6], [-2.2, 1.05], [0, 1.15], [1.6, 1.0], [2.6, 0.55], [3.2, 0.3]]), 'Cabin'))
  const boom = new THREE.CylinderGeometry(0.16, 0.32, 5.6, 14)
  boom.rotateX(Math.PI / 2)
  boom.translate(0, 0.25, 5.8)
  body.add(mesh(boom, 'TailBoom'))
  body.add(mesh(fin([[7.9, 0.1], [8.5, 1.7], [8.9, 1.7], [8.7, 0.1]], 0.1), 'Fin'))
  body.add(mesh(plate([[-1.1, 7.3], [1.1, 7.3], [1.1, 7.8], [-1.1, 7.8]], 0.08), 'Stabiliser'))
  for (const side of [1, -1]) {
    const skid = new THREE.CylinderGeometry(0.07, 0.07, 4.2, 8)
    skid.rotateX(Math.PI / 2)
    skid.translate(side * 1.05, -1.55, -0.2)
    body.add(mesh(skid, side > 0 ? 'SkidRight' : 'SkidLeft'))
    for (const z of [-1.2, 0.9]) {
      const strut = new THREE.CylinderGeometry(0.05, 0.05, 0.75, 6)
      strut.translate(side * 0.95, -1.2, z)
      body.add(mesh(strut, 'Strut'))
    }
  }
  const mast = new THREE.CylinderGeometry(0.12, 0.16, 0.55, 10)
  mast.translate(0, 1.35, -0.3)
  body.add(mesh(mast, 'Mast'))
  const canopy = new THREE.SphereGeometry(0.95, 24, 12, 0, Math.PI * 2, 0, Math.PI / 2)
  canopy.scale(1, 0.8, 1.5)
  canopy.rotateX(-0.35)
  canopy.translate(0, 0.25, -2.0)
  root.add(mesh(canopy, 'Canopy', glass))

  const main = new THREE.Group()
  main.name = 'MainRotor'
  main.position.set(0, 1.65, -0.3)
  main.add(mesh(new THREE.CylinderGeometry(0.22, 0.22, 0.18, 12), 'MainHub'))
  for (let k = 0; k < 4; k++) {
    const blade = new THREE.BoxGeometry(0.32, 0.05, 6)
    blade.translate(0, 0, 3.1)
    blade.rotateY((k * Math.PI) / 2)
    main.add(mesh(blade, `MainBlade${k + 1}`))
  }
  root.add(main)

  const tailRotor = new THREE.Group()
  tailRotor.name = 'TailRotor'
  tailRotor.position.set(0.28, 0.9, 8.6)
  for (let k = 0; k < 2; k++) {
    const blade = new THREE.BoxGeometry(0.04, 0.2, 1.5)
    blade.translate(0, 0, 0.75)
    blade.rotateX(k * Math.PI)
    tailRotor.add(mesh(blade, `TailBlade${k + 1}`))
  }
  root.add(tailRotor)
  return root
}

const exporter = new GLTFExporter()
mkdirSync(out, { recursive: true })
for (const [name, build] of [['jet', jet], ['helicopter', helicopter]]) {
  const glb = await exporter.parseAsync(build(), { binary: true })
  writeFileSync(join(out, `${name}.glb`), Buffer.from(glb))
  console.log(`wrote ${join(out, `${name}.glb`)} (${Buffer.byteLength(Buffer.from(glb))} bytes)`)
}
