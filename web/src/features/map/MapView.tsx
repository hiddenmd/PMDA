import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useRef } from 'react'
import type { Alert, FlightTrack, MapSettings, PlaybackFrame } from '../../api/types'
import { formatElapsed, formatUtcClock } from '../../lib/format'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { ALERTS, clockPosition } from '../alerts/alerts'
import { stepForSpeed, viewAt } from '../playback/playbackView'
import { buildThreats, type ResolvedProfile } from '../rwr/symbology'
import { destination, formatLatLon, groundSpeedAt, headingAt, metresToFeet, msToKnots, positionAt, sampleIndex } from './track'

/** Lines of bearing are drawn this long: RWRs give direction, not range. */
const BEARING_LINE_M = 30_000
const MAX_BEARING_LINES = 6
const READOUT_EVERY_MS = 100

const FIXED_WING =
  '<svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true"><path d="M12 1.5l1.3 7.6 8.2 4.3v1.7l-8.2-2.1-.4 5.6 2.9 2.1v1.5L12 21.1l-3.8 1.1v-1.5l2.9-2.1-.4-5.6-8.2 2.1v-1.7l8.2-4.3z"/></svg>'
const HELICOPTER =
  '<svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true"><circle class="rotor" cx="12" cy="9.5" r="8.5"/><ellipse cx="12" cy="10" rx="2.7" ry="4.3"/><rect x="11.35" y="13.5" width="1.3" height="7.5"/><rect x="9.4" y="19.8" width="5.2" height="1.3"/></svg>'

export interface MapReadout {
  lat: HTMLElement | null
  lon: HTMLElement | null
  alt: HTMLElement | null
  heading: HTMLElement | null
  speed: HTMLElement | null
}

interface MapViewProps {
  actor: PlaybackActor
  track: FlightTrack
  alerts: readonly Alert[]
  settings: MapSettings
  profile: ResolvedProfile | undefined
  startUtc: string
  follow: boolean
  /** Changes when the analyst asks to fit the whole track in view. */
  fitRequest: number
  onFollowChange: (follow: boolean) => void
  readout: React.RefObject<MapReadout>
}

const css = (el: Element, name: string) => getComputedStyle(el).getPropertyValue(name).trim()

/**
 * The flight on a map: whole track dim, flown part bright, the aircraft at the playhead pointing
 * along its track, missile alerts where they happened (click to seek) and lines of bearing to the
 * threats the RWR is tracking. Leaflet is driven imperatively from the playback actor in an
 * animation-frame loop, so playback never re-renders React.
 */
export function MapView({ actor, track, alerts, settings, profile, startUtc, follow, fitRequest, onFollowChange, readout }: MapViewProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<L.Map | null>(null)
  const boundsRef = useRef<L.LatLngBounds | null>(null)
  const platform = profile?.platform
  const live = useRef({ follow, profile, onFollowChange })
  useEffect(() => {
    live.current = { follow, profile, onFollowChange }
  })

  useEffect(() => {
    const container = containerRef.current!
    const map = L.map(container, { preferCanvas: true, zoomControl: true, attributionControl: true, worldCopyJump: true })
    mapRef.current = map
    // replaced in the MapLibre rewrite
    const tileUrl: string | null = null
    if (tileUrl) {
      L.tileLayer(tileUrl, { attribution: settings.attribution ?? '', maxZoom: settings.maxZoom, subdomains: 'abcd' }).addTo(map)
    }
    map.on('dragstart', () => live.current.onFollowChange(false))

    const latLngs = track.t.map((_, i) => L.latLng(track.lat[i], track.lon[i]))
    const navigation = () => css(container, '--color-series-navigation')
    // Still to fly: dashed rather than faint, so it reads on light and dark base maps alike.
    const whole = L.polyline(latLngs, { color: navigation(), weight: 2, opacity: 0.6, dashArray: '5 5', interactive: false }).addTo(map)
    const flown = L.polyline([], { color: navigation(), weight: 3, opacity: 0.95, interactive: false }).addTo(map)
    const bounds = whole.getBounds()
    boundsRef.current = bounds
    if (bounds.isValid()) map.fitBounds(bounds, { padding: [24, 24] })
    else map.setView([0, 0], 2)

    const start = Date.parse(startUtc)
    const endMarker = (i: number, label: string) =>
      L.circleMarker(latLngs[i], { radius: 5, weight: 2, color: css(container, '--color-foreground'), fillColor: css(container, '--color-card'), fillOpacity: 1 })
        .bindTooltip(`${label} ${formatUtcClock(new Date(start + track.t[i]))}`, { direction: 'top' })
        .addTo(map)
    if (latLngs.length > 0) {
      endMarker(0, 'Start')
      endMarker(latLngs.length - 1, 'End')
    }

    // Missile alerts where the aircraft was when they sounded.
    for (const a of alerts) {
      const spec = ALERTS[a.kind]
      if (spec.severity !== 'critical') continue
      const p = positionAt(track, a.offsetMs)
      if (!p) continue
      L.marker([p.lat, p.lon], {
        icon: L.divIcon({ className: 'map-alert', html: '<span aria-hidden="true"></span>', iconSize: [16, 16] }),
        title: spec.label,
        keyboard: true,
      })
        .bindTooltip(`${spec.label} · ${formatElapsed(a.offsetMs)}${a.bearingDeg === null ? '' : ` · ${clockPosition(a.bearingDeg)}`}`, { direction: 'top' })
        .on('click', () => actor.send({ type: 'SEEK', positionMs: a.offsetMs }))
        .addTo(map)
    }

    const helicopter = platform === 'Helicopter'
    const aircraft = L.marker(latLngs[0] ?? [0, 0], {
      icon: L.divIcon({ className: 'map-aircraft', html: helicopter ? HELICOPTER : FIXED_WING, iconSize: [28, 28] }),
      interactive: false,
      keyboard: false,
      zIndexOffset: 1000,
    }).addTo(map)

    const bearingLines = Array.from({ length: MAX_BEARING_LINES }, () => L.polyline([], { weight: 1.5, dashArray: '6 5', interactive: false }).addTo(map))

    const restyle = () => {
      whole.setStyle({ color: navigation() })
      flown.setStyle({ color: navigation() })
    }
    const themeObserver = new MutationObserver(restyle)
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    const resizeObserver = new ResizeObserver(() => map.invalidateSize({ animate: false }))
    resizeObserver.observe(container)

    let last = { position: -1, frames: null as unknown, profile: null as unknown, follow: false }
    let lastIndex = -2
    let lastReadout = Number.NEGATIVE_INFINITY
    let readoutStale = false
    let handle = requestAnimationFrame(function draw(now) {
      handle = requestAnimationFrame(draw)
      const { positionMs, frames, speed } = actor.getSnapshot().context
      const { follow: following, profile: currentProfile } = live.current
      const changed = positionMs !== last.position || frames !== last.frames || currentProfile !== last.profile || following !== last.follow
      if (changed) {
        last = { position: positionMs, frames, profile: currentProfile, follow: following }
        readoutStale = true
        update(positionMs, frames, speed, currentProfile, following)
      }
      // Throttled, but never left stale: a paused playhead still gets its final values.
      if (readoutStale && now - lastReadout >= READOUT_EVERY_MS) {
        lastReadout = now
        readoutStale = false
        writeReadout(positionMs)
      }
    })

    function update(positionMs: number, frames: PlaybackFrame[], speed: number, currentProfile: ResolvedProfile | undefined, following: boolean) {
      const p = positionAt(track, positionMs)
      if (!p) return
      const here = L.latLng(p.lat, p.lon)
      const heading = headingAt(track, positionMs)

      // Flown part: rebuilt when the playhead passes a sample, else only its moving tip.
      const index = sampleIndex(track, positionMs)
      if (index !== lastIndex) {
        flown.setLatLngs([...latLngs.slice(0, index + 1), here])
        lastIndex = index
      } else {
        const points = flown.getLatLngs() as L.LatLng[]
        points[points.length - 1] = here
        flown.setLatLngs(points)
      }

      aircraft.setLatLng(here)
      const svg = aircraft.getElement()?.querySelector('svg')
      if (svg) svg.style.transform = `rotate(${heading ?? 0}deg)`

      // Lines of bearing to tracked or critical threats: true bearing = track + relative bearing
      // (track over the ground stands in for the heading the RWR measured against).
      const threats =
        currentProfile && heading !== null
          ? buildThreats(viewAt(frames, positionMs, stepForSpeed(speed)).emitters, currentProfile)
              .filter((t) => t.locked || t.lethality === 'Critical')
              .slice(0, MAX_BEARING_LINES)
          : []
      bearingLines.forEach((line, i) => {
        const t = threats[i]
        if (!t) {
          line.setLatLngs([])
          return
        }
        line.setLatLngs([here, destination(p.lat, p.lon, (heading! + t.bearingDeg) % 360, BEARING_LINE_M)])
        line.setStyle({ color: css(container, t.locked ? '--color-alert-critical' : '--color-alert-warning'), opacity: 0.85 })
      })

      if (following) {
        // Re-centre only when the aircraft nears the edge, so tiles are not reloaded every frame.
        const inner = map.getBounds().pad(-0.25)
        if (!inner.contains(here)) map.panTo(here, { animate: false })
      }
    }

    function writeReadout(positionMs: number) {
      const p = positionAt(track, positionMs)
      if (!p) return
      const r = readout.current
      const heading = headingAt(track, positionMs)
      const speedMs = groundSpeedAt(track, positionMs)
      if (r.lat) r.lat.textContent = formatLatLon(p.lat, 'lat')
      if (r.lon) r.lon.textContent = formatLatLon(p.lon, 'lon')
      if (r.alt) r.alt.textContent = `${Math.round(p.altM).toLocaleString('en-US')} m · ${Math.round(metresToFeet(p.altM)).toLocaleString('en-US')} ft`
      if (r.heading) r.heading.textContent = heading === null ? '—' : `${Math.round(heading).toString().padStart(3, '0')}°T`
      if (r.speed) r.speed.textContent = speedMs === null ? '—' : `${Math.round(msToKnots(speedMs))} kt`
    }

    return () => {
      cancelAnimationFrame(handle)
      themeObserver.disconnect()
      resizeObserver.disconnect()
      map.remove()
      mapRef.current = null
    }
  }, [actor, track, alerts, settings, startUtc, readout, platform])

  useEffect(() => {
    const map = mapRef.current
    const bounds = boundsRef.current
    if (fitRequest > 0 && map && bounds?.isValid()) map.fitBounds(bounds, { padding: [24, 24] })
  }, [fitRequest])

  return (
    <div className="map-view">
      <div ref={containerRef} className="map-view__map" role="application" aria-label="Flight map: track, aircraft position and missile alerts" />
      {settings.source === 'None' && <p className="map-view__note">No base map configured: track only.</p>}
    </div>
  )
}
