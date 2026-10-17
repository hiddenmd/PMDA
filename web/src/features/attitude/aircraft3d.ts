type Vec = [number, number, number]
type Part = 'body' | 'wing' | 'canopy' | 'rotor'

/** A low-poly model in body axes: x forward, y right, z down, metres. */
interface Model {
  vertices: Vec[]
  faces: { v: number[]; part: Part }[]
}

const mirror = (v: Vec): Vec => [v[0], -v[1], v[2]]

function jet(): Model {
  const vertices: Vec[] = [
    [8, 0, 0.1], // 0 nose
    [3.5, 0, -0.9], [3.5, 0.85, 0], [3.5, 0, 0.6], [3.5, -0.85, 0], // 1-4 forward ring: top, right, bottom, left
    [-6, 0, -0.75], [-6, 0.65, 0], [-6, 0, 0.5], [-6, -0.65, 0], // 5-8 aft ring
    [-8.4, 0, -0.1], // 9 tail
    [2, 0.8, 0.15], [-4.2, 0.65, 0.15], [-4.6, 5.8, 0.25], [-2.6, 5.8, 0.25], // 10-13 right wing
    [-5.6, 0.6, 0], [-7.9, 0.5, 0], [-8.3, 2.9, 0], [-7.1, 2.9, 0], // 14-17 right tailplane
    [-4.9, 0, -0.7], [-7.9, 0, -0.3], [-8.4, 0, -3.3], [-7.1, 0, -3.3], // 18-21 fin
    [5.4, 0, -0.75], [3, 0.5, -0.7], [1.2, 0, -1.25], [3, -0.5, -0.7], // 22-25 canopy
  ]
  const n = vertices.length
  // Left wing and tailplane mirror the right ones.
  for (const i of [10, 11, 12, 13, 14, 15, 16, 17]) vertices.push(mirror(vertices[i]))
  const L = (i: number) => n + [10, 11, 12, 13, 14, 15, 16, 17].indexOf(i)
  return {
    vertices,
    faces: [
      { v: [0, 1, 2], part: 'body' },
      { v: [0, 2, 3], part: 'body' },
      { v: [0, 3, 4], part: 'body' },
      { v: [0, 4, 1], part: 'body' },
      { v: [1, 5, 6, 2], part: 'body' },
      { v: [2, 6, 7, 3], part: 'body' },
      { v: [3, 7, 8, 4], part: 'body' },
      { v: [4, 8, 5, 1], part: 'body' },
      { v: [5, 9, 6], part: 'body' },
      { v: [6, 9, 7], part: 'body' },
      { v: [7, 9, 8], part: 'body' },
      { v: [8, 9, 5], part: 'body' },
      { v: [10, 11, 12, 13], part: 'wing' },
      { v: [L(10), L(11), L(12), L(13)], part: 'wing' },
      { v: [14, 15, 16, 17], part: 'wing' },
      { v: [L(14), L(15), L(16), L(17)], part: 'wing' },
      { v: [18, 19, 20, 21], part: 'wing' },
      { v: [22, 23, 24], part: 'canopy' },
      { v: [22, 24, 25], part: 'canopy' },
    ],
  }
}

function helicopter(): Model {
  const vertices: Vec[] = [
    [3.2, 0, 0.3], // 0 nose
    [2, 0, -1.1], [2, 1, 0], [2, 0, 1.1], [2, -1, 0], // 1-4 cabin front ring
    [-1.5, 0, -1.2], [-1.5, 1, 0], [-1.5, 0, 1], [-1.5, -1, 0], // 5-8 cabin rear ring
    [-9, 0, -0.3], // 9 boom end
    [-8, 0, -0.3], [-9.6, 0, -2.2], [-8.8, 0, -2.2], // 10-12 fin
    [3.1, 0, -0.2], [2.2, 0.85, -0.6], [1.6, 0, -1.05], [2.2, -0.85, -0.6], // 13-16 canopy
  ]
  const rotorAt = vertices.length
  const segments = 20
  for (let i = 0; i < segments; i++) {
    const a = (i / segments) * Math.PI * 2
    vertices.push([Math.cos(a) * 6.8, Math.sin(a) * 6.8, -1.6])
  }
  const skids = vertices.length
  vertices.push([2.4, 0.9, 1.9], [-2.4, 0.9, 1.9], [-2.4, 1.0, 1.75], [2.4, 1.0, 1.75])
  vertices.push([2.4, -0.9, 1.9], [-2.4, -0.9, 1.9], [-2.4, -1.0, 1.75], [2.4, -1.0, 1.75])
  return {
    vertices,
    faces: [
      { v: [0, 1, 2], part: 'body' },
      { v: [0, 2, 3], part: 'body' },
      { v: [0, 3, 4], part: 'body' },
      { v: [0, 4, 1], part: 'body' },
      { v: [1, 5, 6, 2], part: 'body' },
      { v: [2, 6, 7, 3], part: 'body' },
      { v: [3, 7, 8, 4], part: 'body' },
      { v: [4, 8, 5, 1], part: 'body' },
      { v: [5, 9, 6], part: 'body' },
      { v: [6, 9, 7], part: 'body' },
      { v: [7, 9, 8], part: 'body' },
      { v: [8, 9, 5], part: 'body' },
      { v: [10, 9, 11, 12], part: 'wing' },
      { v: [13, 14, 15], part: 'canopy' },
      { v: [13, 15, 16], part: 'canopy' },
      { v: Array.from({ length: segments }, (_, i) => rotorAt + i), part: 'rotor' },
      { v: [skids, skids + 1, skids + 2, skids + 3], part: 'wing' },
      { v: [skids + 4, skids + 5, skids + 6, skids + 7], part: 'wing' },
    ],
  }
}

const MODELS = { FixedWing: jet(), Helicopter: helicopter() }

export interface ModelColors {
  body: string
  wing: string
  canopy: string
  rotor: string
  grid: string
}

const RAD = Math.PI / 180
/** Chase view: from behind, slightly to the right and above. */
const ORBIT = -12 * RAD
const ELEVATION = 14 * RAD
const CAMERA_DISTANCE = 42
const LIGHT: Vec = normalize([-0.4, -0.5, -0.75])

function normalize(v: Vec): Vec {
  const l = Math.hypot(...v) || 1
  return [v[0] / l, v[1] / l, v[2] / l]
}

/** Body axes to the heading-level frame: roll about x, then pitch about y (yaw is the camera's). */
function attitude(v: Vec, pitch: number, roll: number): Vec {
  const [x, y, z] = v
  const cr = Math.cos(roll)
  const sr = Math.sin(roll)
  const y1 = y * cr - z * sr
  const z1 = y * sr + z * cr
  const cp = Math.cos(pitch)
  const sp = Math.sin(pitch)
  return [x * cp + z1 * sp, y1, -x * sp + z1 * cp]
}

/** Heading-level frame to camera axes: x depth (away from the camera), y right, z down on screen. */
function camera(v: Vec): Vec {
  const [x, y, z] = v
  const x1 = x * Math.cos(ORBIT) + y * Math.sin(ORBIT)
  const y1 = -x * Math.sin(ORBIT) + y * Math.cos(ORBIT)
  return [x1 * Math.cos(ELEVATION) + z * Math.sin(ELEVATION), y1, -x1 * Math.sin(ELEVATION) + z * Math.cos(ELEVATION)]
}

function shade(hex: string, factor: number): string {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
  if (!m) return hex
  const n = parseInt(m[1], 16)
  const ch = (s: number) => Math.round(Math.min(255, ((n >> s) & 255) * factor))
  return `rgb(${ch(16)} ${ch(8)} ${ch(0)})`
}

/**
 * The aircraft from a chase position behind it, pitched and banked, over a ground grid that stays
 * level, so bank and climb read at a glance. Yaw is not shown here (the camera follows the heading);
 * the heading tape shows it.
 */
export function drawAircraft(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  size: number,
  platform: 'FixedWing' | 'Helicopter',
  pitchDeg: number,
  rollDeg: number,
  c: ModelColors,
) {
  const scale = size / 22
  const project = (v: Vec): [number, number, number] => {
    const [d, y, z] = camera(v)
    const perspective = CAMERA_DISTANCE / (CAMERA_DISTANCE + d)
    return [cx + y * scale * perspective, cy + z * scale * perspective, d]
  }

  // Level ground grid 7 m below, moving neither with pitch nor roll.
  ctx.save()
  ctx.strokeStyle = c.grid
  ctx.lineWidth = 1
  for (let i = -36; i <= 36; i += 6) {
    for (const [a, b] of [
      [[i, -36, 7], [i, 36, 7]],
      [[-36, i, 7], [36, i, 7]],
    ] as [Vec, Vec][]) {
      const p = project(a)
      const q = project(b)
      ctx.beginPath()
      ctx.moveTo(p[0], p[1])
      ctx.lineTo(q[0], q[1])
      ctx.stroke()
    }
  }
  ctx.restore()

  const model = MODELS[platform]
  const pitch = pitchDeg * RAD
  const roll = rollDeg * RAD
  const world = model.vertices.map((v) => camera(attitude(v, pitch, roll)))
  const screen = model.vertices.map((v) => project(attitude(v, pitch, roll)))

  const faces = model.faces
    .map((f) => {
      const [a, b, d] = [world[f.v[0]], world[f.v[1]], world[f.v[2]]]
      const u: Vec = [b[0] - a[0], b[1] - a[1], b[2] - a[2]]
      const w: Vec = [d[0] - a[0], d[1] - a[1], d[2] - a[2]]
      const normal = normalize([u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0]])
      const light = Math.abs(normal[0] * LIGHT[0] + normal[1] * LIGHT[1] + normal[2] * LIGHT[2])
      const depth = f.v.reduce((sum, i) => sum + world[i][0], 0) / f.v.length
      return { f, depth, brightness: 0.5 + 0.6 * light }
    })
    .sort((a, b) => b.depth - a.depth) // far faces first

  ctx.save()
  ctx.lineJoin = 'round'
  for (const { f, brightness } of faces) {
    ctx.beginPath()
    f.v.forEach((i, k) => (k === 0 ? ctx.moveTo(screen[i][0], screen[i][1]) : ctx.lineTo(screen[i][0], screen[i][1])))
    ctx.closePath()
    ctx.globalAlpha = f.part === 'rotor' ? 0.22 : 1
    ctx.fillStyle = shade(c[f.part], brightness)
    ctx.fill()
    ctx.globalAlpha = f.part === 'rotor' ? 0.5 : 0.35
    ctx.strokeStyle = shade(c[f.part], 0.45)
    ctx.lineWidth = 1
    ctx.stroke()
  }
  ctx.restore()
}
