import type { ResolvedProfile, SymbolShape, ThreatSymbol } from './symbology'

export interface RwrFrame {
  profile: ResolvedProfile
  threats: ThreatSymbol[]
  /** Countermeasures left, when the profile shows the counter. */
  remaining: { chaff: number; flares: number } | null
  persistenceMs: number
  /** Real time in ms, for blinking. */
  now: number
  still: boolean
}

/**
 * Draws an RWR display in the profile's style: bezel, phosphor screen, legends, rings, ticks,
 * own-ship symbol and threat symbology. Pure drawing on a 2D context sized w × h (CSS pixels).
 */
export function drawRwr(ctx: CanvasRenderingContext2D, width: number, height: number, f: RwrFrame) {
  const { display } = f.profile
  const phosphor = display.phosphor
  ctx.clearRect(0, 0, width, height)
  // A square display, centred in whatever space the panel has (like the MFD it imitates).
  const side = Math.min(width, height)
  ctx.save()
  ctx.translate((width - side) / 2, (height - side) / 2)
  drawSquare(ctx, side, side, f, display, phosphor)
  ctx.restore()
}

function drawSquare(ctx: CanvasRenderingContext2D, w: number, h: number, f: RwrFrame, display: RwrFrame['profile']['display'], phosphor: string) {

  // Bezel with chamfered corners, then the screen.
  const m = Math.min(w, h)
  ctx.fillStyle = display.bezel
  chamfer(ctx, 0, 0, w, h, m * 0.07)
  ctx.fill()
  const inset = m * 0.035
  const sx = inset
  const sy = inset
  const sw = w - inset * 2
  const sh = h - inset * 2
  ctx.fillStyle = display.background
  roundRect(ctx, sx, sy, sw, sh, m * 0.03)
  ctx.fill()
  const vignette = ctx.createRadialGradient(w / 2, h / 2, m * 0.2, w / 2, h / 2, m * 0.75)
  vignette.addColorStop(0, 'rgba(0,0,0,0)')
  vignette.addColorStop(1, 'rgba(0,0,0,0.45)')
  ctx.fillStyle = vignette
  roundRect(ctx, sx, sy, sw, sh, m * 0.03)
  ctx.fill()

  // Layout: legend bands top and bottom, scope in the middle.
  const font = Math.max(9, Math.round(m * 0.036))
  const band = font * 2.9
  const cx = w / 2
  const cy = sy + band + (sh - band * 2) / 2
  const R = Math.max(10, Math.min(sw * 0.42, (sh - band * 2) / 2 - font * 1.2))

  ctx.save()
  ctx.strokeStyle = phosphor
  ctx.fillStyle = phosphor
  ctx.lineWidth = Math.max(1, m / 420)
  ctx.lineCap = 'round'
  ctx.shadowColor = phosphor
  ctx.shadowBlur = f.still ? 0 : 5
  ctx.font = `600 ${font}px 'IBM Plex Mono', ui-monospace, monospace`
  ctx.textBaseline = 'middle'

  drawLegends(ctx, f.profile, sx, sy, sw, sh, font)
  if (f.remaining) drawCounter(ctx, f.remaining, sx + font * 0.9, sy + band + font * 0.6, font)

  // Rings and the own-ship circle.
  for (const r of display.rings) circle(ctx, cx, cy, R * r)
  circle(ctx, cx, cy, R * display.ownshipCircle)

  // Ticks outward from the outer ring; the nose tick is longer.
  for (let deg = 0; deg < 360; deg += display.tickEveryDeg) {
    const t = (deg * Math.PI) / 180
    const r0 = R * 1.04
    const r1 = R * (deg === 0 ? 1.12 : 1.08)
    line(ctx, cx + r0 * Math.sin(t), cy - r0 * Math.cos(t), cx + r1 * Math.sin(t), cy - r1 * Math.cos(t))
  }
  if (display.wingMarks) {
    for (const side of [-1, 1]) {
      const x0 = cx + side * R * 0.93
      const x1 = cx + side * R * 1.16
      line(ctx, x0, cy - R * 0.045, x1, cy - R * 0.045)
      line(ctx, x0, cy + R * 0.045, x1, cy + R * 0.045)
      line(ctx, cx + side * R * 1.0, cy, cx + side * R * 1.08, cy)
    }
  }

  drawOwnship(ctx, f.profile.platform, cx, cy, R * display.ownshipCircle)

  // Threats: least important first, so the priority threat is drawn on top.
  const blinkOn = Math.floor(f.now / 250) % 2 === 0
  const points = declutter(f.threats, cx, cy, R, font * 2.1)
  for (let i = f.threats.length - 1; i >= 0; i--) {
    const threat = f.threats[i]
    if (threat.isNew && !f.still && !blinkOn) continue
    ctx.globalAlpha = threat.ageMs > f.persistenceMs / 2 ? 0.55 : 1
    drawSymbol(ctx, threat, points[i].x, points[i].y, font)
  }
  ctx.restore()
}

/**
 * Symbol positions with overlaps removed, as real RWRs do: threats are placed in priority order;
 * one that would land within `minDistance` of an already placed symbol is moved sideways around
 * its ring (alternating sides, growing steps) until clear. The priority threat never moves.
 */
export function declutter(threats: readonly Pick<ThreatSymbol, 'bearingDeg' | 'radius'>[], cx: number, cy: number, R: number, minDistance: number) {
  const placed: { x: number; y: number }[] = []
  for (const t of threats) {
    const at = (bearing: number) => {
      const theta = (bearing * Math.PI) / 180
      return { x: cx + R * t.radius * Math.sin(theta), y: cy - R * t.radius * Math.cos(theta) }
    }
    const clear = (p: { x: number; y: number }) => placed.every((q) => Math.hypot(p.x - q.x, p.y - q.y) >= minDistance)
    let point = at(t.bearingDeg)
    // Angular step that moves about a third of the minimum distance along this ring.
    const step = Math.max(2, (((minDistance / 3) / Math.max(1, R * t.radius)) * 180) / Math.PI)
    for (let k = 1; !clear(point) && k <= 24; k++) point = at(t.bearingDeg + (k % 2 === 1 ? 1 : -1) * Math.ceil(k / 2) * step)
    placed.push(point)
  }
  return placed
}

function drawSymbol(ctx: CanvasRenderingContext2D, t: ThreatSymbol, x: number, y: number, font: number) {
  ctx.textAlign = 'center'
  ctx.fillText(t.code, x, y + font * 0.05)
  const tw = Math.max(ctx.measureText(t.code).width, font * 0.7)
  // Enclosures first; class marks (hat, boat) then sit just outside the largest one, so a
  // priority shield or lock circle never hides that a threat is airborne or ship-borne.
  let extent = font * 0.55
  for (const shape of t.shapes) {
    const half = enclosureHalfHeight(shape, tw, font)
    if (half > 0) {
      drawEnclosure(ctx, shape, x, y, tw, font)
      extent = Math.max(extent, half)
    }
  }
  for (const shape of t.shapes) {
    if (shape === 'Hat' || shape === 'Boat') drawClassMark(ctx, shape, x, y, tw, font, extent)
  }
}

const enclosureSize = (tw: number, font: number) => Math.max(tw, font) * 0.78

/** Half the height of an enclosing shape, 0 for marks that do not enclose. */
function enclosureHalfHeight(shape: SymbolShape, tw: number, font: number): number {
  const s = enclosureSize(tw, font)
  switch (shape) {
    case 'Circle':
      return s * 1.05
    case 'Box':
      return s * 0.85
    case 'Diamond':
      return s * 1.35
    case 'Shield':
      return s * 1.15
    default:
      return 0
  }
}

function drawEnclosure(ctx: CanvasRenderingContext2D, shape: SymbolShape, x: number, y: number, tw: number, font: number) {
  const s = enclosureSize(tw, font)
  ctx.beginPath()
  switch (shape) {
    case 'Circle':
      ctx.arc(x, y, s * 1.05, 0, Math.PI * 2)
      break
    case 'Box':
      ctx.rect(x - s, y - s * 0.85, s * 2, s * 1.7)
      break
    case 'Diamond':
      ctx.moveTo(x, y - s * 1.35)
      ctx.lineTo(x + s * 1.35, y)
      ctx.lineTo(x, y + s * 1.35)
      ctx.lineTo(x - s * 1.35, y)
      ctx.closePath()
      break
    case 'Shield':
      ctx.moveTo(x, y - s * 1.15)
      ctx.lineTo(x + s * 0.95, y - s * 0.6)
      ctx.lineTo(x + s * 0.95, y + s * 0.55)
      ctx.lineTo(x, y + s * 1.05)
      ctx.lineTo(x - s * 0.95, y + s * 0.55)
      ctx.lineTo(x - s * 0.95, y - s * 0.6)
      ctx.closePath()
      break
    default:
      return
  }
  ctx.stroke()
}

/** Airborne "hat" above, ship "boat" below: outside the symbol's enclosure. */
function drawClassMark(ctx: CanvasRenderingContext2D, shape: 'Hat' | 'Boat', x: number, y: number, tw: number, font: number, extent: number) {
  const half = Math.max(tw / 2 + font * 0.25, extent * 0.85)
  const gap = font * 0.18
  ctx.beginPath()
  if (shape === 'Hat') {
    ctx.moveTo(x - half, y - extent - gap)
    ctx.lineTo(x, y - extent - gap - font * 0.42)
    ctx.lineTo(x + half, y - extent - gap)
  } else {
    ctx.moveTo(x - half, y + extent + gap)
    ctx.quadraticCurveTo(x, y + extent + gap + font * 0.55, x + half, y + extent + gap)
  }
  ctx.stroke()
}

function drawOwnship(ctx: CanvasRenderingContext2D, platform: string, cx: number, cy: number, r: number) {
  const u = r * 0.55
  if (platform === 'Helicopter') {
    // Rotor disc, fuselage, tail boom and tail rotor.
    ctx.save()
    ctx.setLineDash([u * 0.25, u * 0.2])
    circle(ctx, cx, cy - u * 0.2, u * 0.75)
    ctx.restore()
    line(ctx, cx, cy - u * 0.55, cx, cy + u * 0.15)
    line(ctx, cx, cy + u * 0.15, cx, cy + u * 1.0)
    line(ctx, cx - u * 0.3, cy + u * 1.0, cx + u * 0.3, cy + u * 1.0)
    return
  }
  // Fixed wing: wings, fuselage and tail plane.
  line(ctx, cx - u, cy - u * 0.15, cx + u, cy - u * 0.15)
  line(ctx, cx, cy - u * 0.6, cx, cy + u * 0.75)
  line(ctx, cx - u * 0.35, cy + u * 0.75, cx + u * 0.35, cy + u * 0.75)
}

function drawLegends(ctx: CanvasRenderingContext2D, p: ResolvedProfile, sx: number, sy: number, sw: number, sh: number, font: number) {
  const pad = font * 0.9
  for (const legend of p.display.legends) {
    const top = legend.position.startsWith('Top')
    const align: CanvasTextAlign = legend.position.endsWith('Left') ? 'left' : legend.position.endsWith('Right') ? 'right' : 'center'
    const x = align === 'left' ? sx + pad : align === 'right' ? sx + sw - pad : sx + sw / 2
    const y1 = top ? sy + pad + font * 0.2 : sy + sh - pad - font * (legend.sub ? 1.3 : 0.2)
    ctx.textAlign = align
    ctx.fillText(legend.text, x, y1)
    if (legend.boxed) {
      const tw = ctx.measureText(legend.text).width
      const left = align === 'left' ? x : align === 'right' ? x - tw : x - tw / 2
      ctx.strokeRect(left - font * 0.2, y1 - font * 0.62, tw + font * 0.4, font * 1.2)
    }
    if (legend.sub) ctx.fillText(legend.sub, x, y1 + font * 1.1)
  }
}

function drawCounter(ctx: CanvasRenderingContext2D, left: { chaff: number; flares: number }, x: number, y: number, font: number) {
  ctx.textAlign = 'center'
  const col = font * 1.7
  ctx.fillText('C', x + col * 0.3, y)
  ctx.fillText('F', x + col * 1.3, y)
  ctx.fillText(String(Math.max(0, left.chaff)), x + col * 0.3, y + font * 1.1)
  ctx.fillText(String(Math.max(0, left.flares)), x + col * 1.3, y + font * 1.1)
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

function chamfer(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, c: number) {
  ctx.beginPath()
  ctx.moveTo(x + c, y)
  ctx.lineTo(x + w - c, y)
  ctx.lineTo(x + w, y + c)
  ctx.lineTo(x + w, y + h - c)
  ctx.lineTo(x + w - c, y + h)
  ctx.lineTo(x + c, y + h)
  ctx.lineTo(x, y + h - c)
  ctx.lineTo(x, y + c)
  ctx.closePath()
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  ctx.beginPath()
  ctx.roundRect(x, y, w, h, r)
}
