/** Colours for the attitude display, read from CSS tokens so both themes work. */
export interface AdiColors {
  sky: string
  ground: string
  /** Sky colour far above the horizon (gradient from `sky` at the horizon). */
  skyHigh: string
  /** Ground colour far below the horizon (gradient from `ground` at the horizon). */
  groundLow: string
  /** Lubber line and other live markers (the theme's primary). */
  accent: string
  line: string
  symbol: string
  text: string
  muted: string
  bezel: string
  /** Tape background (the theme's panel colour; drawn at 55% opacity). */
  panel: string
}

const RAD = Math.PI / 180
const BANK_MARKS = [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]
/** Degrees of pitch visible from the centre to the edge of the ball. */
const PITCH_SPAN = 30

/**
 * Attitude indicator: sky and ground turning with roll and sliding with pitch, a pitch ladder every
 * 5°, a fixed bank scale with a pointer that turns with roll, and the fixed aircraft symbol.
 */
export function drawAdi(ctx: CanvasRenderingContext2D, cx: number, cy: number, r: number, pitch: number, roll: number, c: AdiColors) {
  const ppd = r / PITCH_SPAN // pixels per degree of pitch
  const line = Math.max(1, r / 110)
  const font = Math.max(9, Math.round(r * 0.085))

  ctx.save()
  ctx.beginPath()
  ctx.arc(cx, cy, r, 0, Math.PI * 2)
  ctx.clip()
  ctx.translate(cx, cy)
  ctx.rotate(-roll * RAD)

  const horizon = pitch * ppd
  const skyFill = ctx.createLinearGradient(0, horizon - r * 1.2, 0, horizon)
  skyFill.addColorStop(0, c.skyHigh)
  skyFill.addColorStop(1, c.sky)
  ctx.fillStyle = skyFill
  ctx.fillRect(-2 * r, -3 * r, 4 * r, 3 * r + horizon)
  const groundFill = ctx.createLinearGradient(0, horizon, 0, horizon + r * 1.2)
  groundFill.addColorStop(0, c.ground)
  groundFill.addColorStop(1, c.groundLow)
  ctx.fillStyle = groundFill
  ctx.fillRect(-2 * r, horizon, 4 * r, 3 * r)

  ctx.strokeStyle = c.line
  ctx.fillStyle = c.line
  ctx.lineWidth = line * 1.5
  ctx.beginPath()
  ctx.moveTo(-2 * r, horizon)
  ctx.lineTo(2 * r, horizon)
  ctx.stroke()

  ctx.lineWidth = Math.max(1, line * 0.8)
  ctx.font = `400 ${font}px 'IBM Plex Mono', ui-monospace, monospace`
  ctx.textBaseline = 'middle'
  // The ladder stays inside the bank scale.
  ctx.beginPath()
  ctx.arc(0, 0, r * 0.72, 0, Math.PI * 2)
  ctx.clip()
  for (let p = -90; p <= 90; p += 2.5) {
    if (p === 0) continue
    const y = (pitch - p) * ppd
    if (Math.abs(y) > r * 0.62) continue // whole lines only: no half-clipped labels at the edge
    const half = p % 10 === 0 ? r * 0.26 : p % 5 === 0 ? r * 0.14 : r * 0.07
    ctx.beginPath()
    ctx.moveTo(-half, y)
    ctx.lineTo(half, y)
    ctx.stroke()
    if (p % 10 === 0) {
      const label = String(Math.abs(p))
      ctx.textAlign = 'right'
      ctx.fillText(label, -half - font * 0.4, y)
      ctx.textAlign = 'left'
      ctx.fillText(label, half + font * 0.4, y)
    }
  }
  ctx.restore()

  // Bank scale: fixed marks on top, a pointer that turns with the horizon.
  ctx.save()
  ctx.translate(cx, cy)
  ctx.strokeStyle = c.line
  ctx.lineWidth = line
  for (const b of BANK_MARKS) {
    const a = (b - 90) * RAD
    const long = b % 30 === 0
    const inner = r * (long ? 0.84 : 0.9)
    ctx.beginPath()
    ctx.moveTo(Math.cos(a) * inner, Math.sin(a) * inner)
    ctx.lineTo(Math.cos(a) * r * 0.97, Math.sin(a) * r * 0.97)
    ctx.stroke()
  }
  ctx.rotate(-roll * RAD)
  ctx.fillStyle = c.symbol
  ctx.beginPath()
  ctx.moveTo(0, -r * 0.83)
  ctx.lineTo(-r * 0.05, -r * 0.74)
  ctx.lineTo(r * 0.05, -r * 0.74)
  ctx.closePath()
  ctx.fill()
  // Slip/skid indicator: centred, because derived attitude has no sideslip.
  ctx.beginPath()
  ctx.moveTo(-r * 0.055, -r * 0.715)
  ctx.lineTo(r * 0.055, -r * 0.715)
  ctx.lineTo(r * 0.07, -r * 0.68)
  ctx.lineTo(-r * 0.07, -r * 0.68)
  ctx.closePath()
  ctx.fill()
  ctx.restore()

  // Fixed aircraft symbol: wings and a centre dot.
  ctx.save()
  ctx.translate(cx, cy)
  ctx.strokeStyle = c.symbol
  ctx.fillStyle = c.symbol
  ctx.lineWidth = Math.max(2, r / 40)
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  const w = r * 0.42
  ctx.beginPath()
  ctx.moveTo(-w, 0)
  ctx.lineTo(-w * 0.35, 0)
  ctx.lineTo(-w * 0.2, r * 0.08)
  ctx.moveTo(w, 0)
  ctx.lineTo(w * 0.35, 0)
  ctx.lineTo(w * 0.2, r * 0.08)
  ctx.stroke()
  ctx.beginPath()
  ctx.arc(0, 0, Math.max(2, r / 34), 0, Math.PI * 2)
  ctx.fill()
  ctx.restore()

  ctx.strokeStyle = c.bezel
  ctx.lineWidth = Math.max(2, r / 30)
  ctx.beginPath()
  ctx.arc(cx, cy, r, 0, Math.PI * 2)
  ctx.stroke()
}

const CARDINAL: Record<number, string> = { 0: 'N', 90: 'E', 180: 'S', 270: 'W' }

/** Heading tape: ±30° around the heading, ticks every 5°, labels every 10° (N/E/S/W at the cardinals). */
export function drawHeadingTape(ctx: CanvasRenderingContext2D, x: number, y: number, width: number, height: number, heading: number, c: AdiColors) {
  const span = 30
  const ppd = width / (span * 2)
  const font = Math.max(9, Math.round(height * 0.42))
  const cx = x + width / 2

  ctx.save()
  ctx.beginPath()
  ctx.rect(x, y, width, height)
  ctx.clip()
  ctx.globalAlpha = 0.55
  ctx.fillStyle = c.panel
  ctx.fillRect(x, y, width, height)
  ctx.globalAlpha = 1
  ctx.strokeStyle = c.muted
  ctx.fillStyle = c.text
  ctx.lineWidth = 1
  ctx.font = `400 ${font}px 'IBM Plex Mono', ui-monospace, monospace`
  ctx.textAlign = 'center'
  ctx.textBaseline = 'top'
  const first = Math.ceil((heading - span) / 5) * 5
  for (let h = first; h <= heading + span; h += 5) {
    const px = Math.round(cx + (h - heading) * ppd) + 0.5
    const major = h % 10 === 0
    ctx.beginPath()
    ctx.moveTo(px, y)
    ctx.lineTo(px, y + height * (major ? 0.32 : 0.18))
    ctx.stroke()
    if (major) {
      const deg = ((h % 360) + 360) % 360
      ctx.fillText(CARDINAL[deg] ?? String(deg / 10).padStart(2, '0'), px, y + height * 0.4)
    }
  }
  ctx.restore()

  // Lubber line pointing at the current heading.
  ctx.fillStyle = c.accent
  ctx.beginPath()
  ctx.moveTo(cx, y + height * 0.3)
  ctx.lineTo(cx - height * 0.18, y)
  ctx.lineTo(cx + height * 0.18, y)
  ctx.closePath()
  ctx.fill()
}
