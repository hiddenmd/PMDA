import type { Alert } from '../../api/types'
import { ALERTS, SEVERITY_RANK, type TonePattern } from './alerts'

/** One tone: start and length in seconds, frequency in Hz, optionally sweeping to `to` Hz. */
interface Note {
  at: number
  dur: number
  freq: number
  to?: number
}

const repeat = (count: number, every: number, note: Omit<Note, 'at'>, alternate?: number): Note[] =>
  Array.from({ length: count }, (_, i) => ({ ...note, at: i * every, freq: alternate && i % 2 ? alternate : note.freq }))

/** Synthetic cockpit tones in the style of US RWR/MWS audio; not any real system's exact tones. */
const PATTERNS: Record<TonePattern, Note[]> = {
  newThreat: repeat(3, 0.15, { dur: 0.08, freq: 1250 }), // three short pips
  lock: repeat(5, 0.16, { dur: 0.1, freq: 900 }), // steady pulsed tone
  launch: repeat(16, 0.075, { dur: 0.07, freq: 1600 }, 1000), // fast high/low warble
  approach: repeat(4, 0.3, { dur: 0.26, freq: 700, to: 1700 }), // rising sweeps
  chime: [
    { at: 0, dur: 0.18, freq: 760 },
    { at: 0.2, dur: 0.25, freq: 520 },
  ],
}

// Square waves are harsh: 0.15 (about -16 dBFS) is clearly audible without drowning the voice warnings.
const VOLUME = 0.15

/**
 * Plays alert tones and voice warnings. A more severe warning cuts off a lesser one; a lesser one
 * never interrupts a more severe one. Voices are the browser's on-device ones only, so warning text
 * never leaves the machine. Silently does nothing where Web Audio is unavailable.
 */
export class AlertSound {
  private context: AudioContext | null = null
  private current: { rank: number; until: number; nodes: OscillatorNode[] } | null = null
  private voiceTimer: ReturnType<typeof setTimeout> | undefined

  play(alert: Alert, withVoice: boolean) {
    const ctx = this.audio()
    if (!ctx) return
    const spec = ALERTS[alert.kind]
    const rank = SEVERITY_RANK[spec.severity]
    const now = ctx.currentTime
    if (this.current && this.current.until > now) {
      if (this.current.rank > rank) return
      this.stop()
    }

    const out = ctx.createGain()
    out.gain.value = VOLUME
    out.connect(ctx.destination)
    const nodes: OscillatorNode[] = []
    let end = now
    for (const n of PATTERNS[spec.tone]) {
      const t0 = now + n.at
      const t1 = t0 + n.dur
      const osc = ctx.createOscillator()
      osc.type = 'square'
      osc.frequency.setValueAtTime(n.freq, t0)
      if (n.to) osc.frequency.linearRampToValueAtTime(n.to, t1)
      // Short ramps at both ends: hard edges click.
      const envelope = ctx.createGain()
      envelope.gain.setValueAtTime(0, t0)
      envelope.gain.linearRampToValueAtTime(1, t0 + 0.005)
      envelope.gain.setValueAtTime(1, t1 - 0.01)
      envelope.gain.linearRampToValueAtTime(0, t1)
      osc.connect(envelope).connect(out)
      osc.start(t0)
      osc.stop(t1 + 0.01)
      nodes.push(osc)
      end = Math.max(end, t1)
    }

    const text = withVoice ? spec.voice?.(alert) : undefined
    const speechSeconds = text ? text.length * 0.07 : 0
    this.current = { rank, until: end + speechSeconds, nodes }
    if (text) this.voiceTimer = setTimeout(() => speak(text), (end - now) * 1000)
  }

  stop() {
    clearTimeout(this.voiceTimer)
    if (typeof speechSynthesis !== 'undefined') speechSynthesis.cancel()
    for (const node of this.current?.nodes ?? []) {
      try {
        node.stop()
      } catch {
        // already stopped
      }
    }
    this.current = null
  }

  dispose() {
    this.stop()
    void this.context?.close()
    this.context = null
  }

  private audio(): AudioContext | null {
    if (typeof AudioContext === 'undefined') return null
    this.context ??= new AudioContext()
    // Created before any click on the page, it starts suspended; pressing Play counts as the click.
    if (this.context.state === 'suspended') void this.context.resume()
    return this.context
  }
}

function speak(text: string) {
  if (typeof speechSynthesis === 'undefined') return
  const voice = speechSynthesis.getVoices().find((v) => v.localService && v.lang.startsWith('en'))
  if (!voice) return // network voices would send the warning text to a third party
  const utterance = new SpeechSynthesisUtterance(text)
  utterance.voice = voice
  utterance.rate = 1.15
  utterance.volume = 0.9
  speechSynthesis.speak(utterance)
}
