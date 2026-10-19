import { declutter, strokeWidth, withAlpha, type RwrFrame } from './rwrDraw'
import type { Lethality, ThreatSymbol } from './symbology'

/** HUD colour tokens, read from CSS by the caller so the view follows the theme. */
export interface TacticalTokens {
  background: string
  foreground: string
  muted: string
  border: string
  borderStrong: string
  primary: string
  critical: string
  warning: string
}

const MONO = `'IBM Plex Mono', ui-monospace, monospace`
const DISPLAY = `'Saira Condensed', 'Arial Narrow', system-ui, sans-serif`
const RAD = Math.PI / 180

/** Same colours as the map's estimated sites and bearing lines. */
export function lethalityColor(lethality: Lethality, t: TacticalTokens): string {
  return lethality === 'Critical' ? t.critical : lethality === 'Lethal' ? t.warning : t.muted
}

/** Relative bearing on the rose: 000 is the nose. */
export const roseLabel = (deg: number) => String(((deg % 360) + 360) % 360).padStart(3, '0')

/** The status strip: threat count, the priority threat and its bearing, lock, and countermeasures left. */
export function statusParts(threats: readonly ThreatSymbol[], remaining: { chaff: number; flares: number } | null): string[] {
  const parts: string[] = []
  if (threats.length === 0) parts.push('NO THREATS')
  else {
    const top = threats[0]
    parts.push(`${threats.length} THREAT${threats.length === 1 ? '' : 'S'}`, `PRI ${top.code} ${roseLabel(Math.round(top.bearingDeg) % 360)}°`)
    if (top.locked) parts.push('LOCK')
  }
  if (remaining) parts.push(`CHAFF ${Math.max(0, remaining.chaff)}`, `FLARE ${Math.max(0, remaining.flares)}`)
  return parts
}

/**
 * The RWR in the Tactical HUD style: the same threats the profile places (bearing, ring, priority, lock), drawn as
 * the map draws them — lethality-coloured diamonds with condensed codes, bearing spokes to locked emitters — on a
 * quiet rose with three-digit relative bearings. Pure drawing on a 2D context sized w × h (device pixels).
 */
export function drawTactical(ctx: CanvasRenderingContext2D, width: number, height: number, f: RwrFrame, t: TacticalTokens) {
  ctx.clearRect(0, 0, width, height)
  const side = Math.min(width, height)
  const font = Math.max(10, Math.round(side * 0.032))
  const lw = strokeWidth(side)
  const strip = font * 2.2
  const cx = width / 2
  const cy = (height - side) / 2 + strip + (side - strip) / 2
  // Room outside the outer ring for the ticks and the rose labels.
  const R = Math.max(10, (side - strip) / 2 - font * 2.4)
  const { display } = f.profile

  ctx.save()
  ctx.lineWidth = lw
  ctx.lineCap = 'butt'
  ctx.textBaseline = 'middle'

  drawStatus(ctx, f, t, (width - side) / 2 + font * 0.6, (height - side) / 2 + font * 0.9, font)

  // Faint disc so the scope reads as an instrument on any panel background.
  ctx.fillStyle = withAlpha(t.primary, 0.035)
  ctx.beginPath()
  ctx.arc(cx, cy, R, 0, Math.PI * 2)
  ctx.fill()

  // Rings: the profile's in the strong border colour; the outer one in dim cyan, like the map's coastline.
  ctx.strokeStyle = t.borderStrong
  for (const r of display.rings) circle(ctx, cx, cy, R * r)
  ctx.strokeStyle = withAlpha(t.primary, 0.45)
  circle(ctx, cx, cy, R)

  // Rose: ticks every 10°, longer every 30° with three-digit labels; the nose in cyan.
  ctx.font = `400 ${Math.round(font * 0.85)}px ${MONO}`
  ctx.textAlign = 'center'
  for (let deg = 0; deg < 360; deg += 10) {
    const major = deg % 30 === 0
    const a = deg * RAD
    const r0 = R * 1.015
    const r1 = R * (major ? 1.07 : 1.04)
    ctx.strokeStyle = deg === 0 ? t.primary : major ? t.muted : t.borderStrong
    line(ctx, cx + r0 * Math.sin(a), cy - r0 * Math.cos(a), cx + r1 * Math.sin(a), cy - r1 * Math.cos(a))
    if (major) {
      const rl = R * 1.07 + font * 1.05
      ctx.fillStyle = deg === 0 ? t.primary : t.muted
      ctx.fillText(roseLabel(deg), cx + rl * Math.sin(a), cy - rl * Math.cos(a))
    }
  }
  // Nose caret just inside the outer ring.
  ctx.fillStyle = t.primary
  ctx.beginPath()
  ctx.moveTo(cx, cy - R * 0.985)
  ctx.lineTo(cx - font * 0.4, cy - R * 0.985 + font * 0.6)
  ctx.lineTo(cx + font * 0.4, cy - R * 0.985 + font * 0.6)
  ctx.closePath()
  ctx.fill()

  drawOwnship(ctx, f.profile.platform, cx, cy, Math.max(font * 0.9, R * display.ownshipCircle * 0.75), t)

  // Threats: least important first, so the priority threat is drawn on top.
  const blinkOn = Math.floor(f.now / 250) % 2 === 0
  const points = declutter(f.threats, cx, cy, R, font * 2.4)
  for (let i = f.threats.length - 1; i >= 0; i--) {
    const threat = f.threats[i]
    if (threat.isNew && !f.still && !blinkOn) continue
    ctx.globalAlpha = threat.ageMs > f.persistenceMs / 2 ? 0.55 : 1
    drawThreat(ctx, threat, points[i].x, points[i].y, cx, cy, font, lw, t)
  }
  ctx.restore()
}

function drawStatus(ctx: CanvasRenderingContext2D, f: RwrFrame, t: TacticalTokens, x: number, y: number, font: number) {
  ctx.font = `400 ${Math.round(font * 0.9)}px ${MONO}`
  ctx.textAlign = 'left'
  const top = f.threats[0]
  let at = x
  for (const part of statusParts(f.threats, f.remaining)) {
    ctx.fillStyle = part === 'LOCK' ? t.critical : part.startsWith('PRI') && top ? lethalityColor(top.lethality, t) : t.muted
    ctx.fillText(part, at, y)
    at += ctx.measureText(part).width + font * 1.2
  }
}

function drawThreat(ctx: CanvasRenderingContext2D, th: ThreatSymbol, x: number, y: number, cx: number, cy: number, font: number, lw: number, t: TacticalTokens) {
  const color = lethalityColor(th.lethality, t)
  const d = font * 0.62

  if (th.locked) {
    // Bearing spoke from own-ship, as the map's line of bearing.
    ctx.save()
    ctx.strokeStyle = withAlpha(color, 0.55)
    ctx.setLineDash([font * 0.35, font * 0.3])
    line(ctx, cx, cy, x, y)
    ctx.restore()
    ctx.strokeStyle = t.critical
    circle(ctx, x, y, d * 1.75)
  }

  // Diamond: outlined, filled solid when the emitter is tracking the aircraft.
  ctx.beginPath()
  ctx.moveTo(x, y - d)
  ctx.lineTo(x + d, y)
  ctx.lineTo(x, y + d)
  ctx.lineTo(x - d, y)
  ctx.closePath()
  ctx.fillStyle = withAlpha(color, th.locked ? 0.9 : 0.22)
  ctx.fill()
  ctx.strokeStyle = color
  ctx.lineWidth = lw * 1.4
  ctx.stroke()
  ctx.lineWidth = lw

  // Airborne hat above, naval boat below, as on the cockpit display.
  const marks = th.shapes.filter((s) => s === 'Hat' || s === 'Boat')
  for (const mark of marks) {
    ctx.beginPath()
    if (mark === 'Hat') {
      ctx.moveTo(x - d, y - d * 1.45)
      ctx.lineTo(x, y - d * 2.05)
      ctx.lineTo(x + d, y - d * 1.45)
    } else {
      ctx.moveTo(x - d, y + d * 1.45)
      ctx.quadraticCurveTo(x, y + d * 2.2, x + d, y + d * 1.45)
    }
    ctx.stroke()
  }

  // Code beside the diamond, in the display face, with a background halo for legibility over rings.
  ctx.font = `600 ${font}px ${DISPLAY}`
  ctx.textAlign = 'left'
  const tx = x + d * 1.5
  ctx.lineWidth = Math.max(2, font * 0.25)
  ctx.strokeStyle = t.background
  ctx.strokeText(th.code, tx, y)
  ctx.lineWidth = lw
  ctx.fillStyle = color
  ctx.fillText(th.code, tx, y)

  // The priority threat gets HUD corner brackets.
  if (th.priority) {
    const tw = ctx.measureText(th.code).width
    const left = x - d * 1.9
    const right = tx + tw + d * 0.5
    const top = y - d * 1.9
    const bottom = y + d * 1.9
    const c = d * 0.7
    ctx.strokeStyle = t.foreground
    for (const [px, py, sx, sy] of [
      [left, top, 1, 1],
      [right, top, -1, 1],
      [left, bottom, 1, -1],
      [right, bottom, -1, -1],
    ]) {
      ctx.beginPath()
      ctx.moveTo(px + sx * c, py)
      ctx.lineTo(px, py)
      ctx.lineTo(px, py + sy * c)
      ctx.stroke()
    }
  }
}

/** The map's own-ship shapes, in cyan: a jet silhouette, or a rotor disc and fuselage. */
function drawOwnship(ctx: CanvasRenderingContext2D, platform: string, cx: number, cy: number, s: number, t: TacticalTokens) {
  ctx.fillStyle = t.primary
  ctx.strokeStyle = t.primary
  if (platform === 'Helicopter') {
    ctx.save()
    ctx.setLineDash([s * 0.2, s * 0.15])
    circle(ctx, cx, cy - s * 0.15, s * 0.8)
    ctx.restore()
    ctx.beginPath()
    ctx.ellipse(cx, cy - s * 0.1, s * 0.22, s * 0.38, 0, 0, Math.PI * 2)
    ctx.fill()
    ctx.fillRect(cx - s * 0.05, cy + s * 0.25, s * 0.1, s * 0.6)
    ctx.fillRect(cx - s * 0.22, cy + s * 0.8, s * 0.44, s * 0.08)
    return
  }
  // Nose up; same proportions as the map's FIXED_WING symbol (24-unit box).
  const u = s / 10.5
  const pts: [number, number][] = [
    [0, -10.5], [1.3, -2.9], [9.5, 1.4], [9.5, 3.1], [1.3, 1], [0.9, 6.6], [3.8, 8.7], [3.8, 10.2],
    [0, 9.1], [-3.8, 10.2], [-3.8, 8.7], [-0.9, 6.6], [-1.3, 1], [-9.5, 3.1], [-9.5, 1.4], [-1.3, -2.9],
  ]
  ctx.beginPath()
  pts.forEach(([px, py], i) => (i === 0 ? ctx.moveTo(cx + px * u, cy + py * u) : ctx.lineTo(cx + px * u, cy + py * u)))
  ctx.closePath()
  ctx.fill()
}

function circle(ctx: CanvasRenderingContext2D, x: number, y: number, r: number) {
  ctx.beginPath()
  ctx.arc(x, y, Math.max(0.5, r), 0, Math.PI * 2)
  ctx.stroke()
}

function line(ctx: CanvasRenderingContext2D, x0: number, y0: number, x1: number, y1: number) {
  ctx.beginPath()
  ctx.moveTo(x0, y0)
  ctx.lineTo(x1, y1)
  ctx.stroke()
}
