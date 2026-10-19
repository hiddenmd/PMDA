import { useEffect, useRef, useState } from 'react'
import type * as GeoJSON from 'geojson'
import type { GeoJSONSource, Map as MlMap, Marker } from 'maplibre-gl'
import type { Alert, EmitterFix, FlightTrack, MapSettings, PlaybackFrame } from '../../api/types'
import { formatElapsed, formatUtcClock } from '../../lib/format'
import type { PlaybackActor } from '../../workspace/workspaceContext'
import { ALERTS, clockPosition } from '../alerts/alerts'
import { stepForSpeed, viewAt } from '../playback/playbackView'
import { buildThreats, classify, type ResolvedProfile } from '../rwr/symbology'
import { circlePolygon, ellipsePolygon, graticule, NOMINAL_RANGE_M, wedgePolygon } from './geo'
import type { StyleTokens } from './style/tacticalStyle'
import { destination, formatLatLon, groundSpeedAt, headingAt, metresToFeet, msToKnots, positionAt, sampleIndex } from './track'

/** Lines of bearing are drawn this long: RWRs give direction, not range. */
const BEARING_LINE_M = 30_000
const MAX_BEARING_LINES = 6
const READOUT_EVERY_MS = 100
const SWEEP_PERIOD_MS = 3_000
const SWEEP_WIDTH_DEG = 30
/** Sweep radius around a locked site whose class has no nominal range. */
const SWEEP_FALLBACK_M = 8_000

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
  /** Estimated emitter sites for this flight (server-side bearing fixes). */
  fixes: readonly EmitterFix[]
  mode: '2d' | '3d'
}

const css = (el: Element, name: string) => getComputedStyle(el).getPropertyValue(name).trim()
const empty: GeoJSON.FeatureCollection = { type: 'FeatureCollection', features: [] }
const lineString = (coordinates: [number, number][]): GeoJSON.Feature<GeoJSON.LineString> => ({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates } })

/**
 * The flight on a keyless vector map in the Tactical HUD style (MapLibre, loaded on first use): whole track dashed,
 * flown part glowing, the aircraft at the playhead, missile alerts (click to seek), lines of bearing to tracked
 * threats, and ESTIMATED emitter sites with 95% ellipses, nominal range rings and a sweep while they track the
 * aircraft. Driven imperatively from the playback actor, so playback never re-renders React.
 */
export function MapView(props: MapViewProps) {
  const { actor, track, alerts, settings, profile, startUtc, fitRequest, readout, fixes, mode } = props
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MlMap | null>(null)
  const boundsRef = useRef<[[number, number], [number, number]] | null>(null)
  // MapLibre needs WebGL 2; without it only the readouts follow the playhead.
  const webgl = typeof WebGL2RenderingContext !== 'undefined'
  const [failed, setFailed] = useState(false)
  const unavailable = !webgl || failed
  const platform = profile?.platform
  const live = useRef({ follow: props.follow, profile, onFollowChange: props.onFollowChange, mode })
  useEffect(() => {
    live.current = { follow: props.follow, profile, onFollowChange: props.onFollowChange, mode }
  })

  useEffect(() => {
    const container = containerRef.current!
    if (!webgl) return
    let disposed = false
    let map: MlMap | null = null
    let handle = 0
    const markers: Marker[] = []
    const observers: { disconnect(): void }[] = []

    void Promise.all([import('./engine/maplibre'), import('./style/tacticalStyle')])
      .then(([engine, style]) => {
        if (disposed) return
        const maplibregl = engine.loadMapLibre()
        const tokens = (): StyleTokens => ({
          background: css(container, '--color-background'),
          muted: css(container, '--color-muted'),
          border: css(container, '--color-border'),
          borderStrong: css(container, '--color-border-strong'),
          foreground: css(container, '--color-foreground'),
          mutedForeground: css(container, '--color-muted-foreground'),
          primary: css(container, '--color-primary'),
        })
        const theme = () => (document.documentElement.dataset.theme === 'light' ? 'light' : 'dark')
        const source = (id: string) => map?.getSource(id) as GeoJSONSource | undefined

        const coords = track.t.map((_, i) => [track.lon[i], track.lat[i]] as [number, number])
        const bounds: [[number, number], [number, number]] = [
          [Math.min(...track.lon), Math.min(...track.lat)],
          [Math.max(...track.lon), Math.max(...track.lat)],
        ]
        boundsRef.current = bounds

        map = new maplibregl.Map({
          container,
          style: style.tacticalStyle(theme(), settings, tokens()),
          bounds,
          fitBoundsOptions: { padding: 24 },
          maxZoom: settings.maxZoom,
          attributionControl: { compact: true },
        })
        mapRef.current = map
        map.addControl(new maplibregl.NavigationControl({ visualizePitch: true }), 'top-left')
        map.on('dragstart', () => live.current.onFollowChange(false))

        // Fires for the first style and again after every theme change, which replaces the style and with it every
        // source and layer added here. HTML markers survive by design.
        let overlays = false
        map.on('style.load', () => {
          if (!map!.getSource('track-whole')) addOverlays()
        })

        function addOverlays() {
          const m = map!
          const pad = 2
          m.addSource('graticule', { type: 'geojson', data: graticule({ west: bounds[0][0] - pad, south: bounds[0][1] - pad, east: bounds[1][0] + pad, north: bounds[1][1] + pad }, 0.25) })
          m.addLayer({ id: 'graticule', type: 'line', source: 'graticule', paint: { 'line-color': tokens().border, 'line-width': 0.6, 'line-opacity': 0.7 } })

          m.addSource('fix-ellipses', { type: 'geojson', data: empty })
          m.addLayer({ id: 'fix-ellipses', type: 'line', source: 'fix-ellipses', paint: { 'line-color': ['get', 'color'], 'line-width': 1.2, 'line-dasharray': [3, 2] } })
          m.addSource('fix-rings', { type: 'geojson', data: empty })
          m.addLayer({ id: 'fix-rings', type: 'line', source: 'fix-rings', paint: { 'line-color': ['get', 'color'], 'line-width': 1, 'line-opacity': 0.35 } })
          m.addSource('sweep', { type: 'geojson', data: empty })
          m.addLayer({ id: 'sweep', type: 'fill', source: 'sweep', paint: { 'fill-color': ['get', 'color'], 'fill-opacity': 0.25 } })

          const nav = css(container, '--color-primary')
          // Still to fly: dashed rather than faint, so it reads on light and dark base maps alike.
          m.addSource('track-whole', { type: 'geojson', data: lineString(coords) })
          m.addLayer({ id: 'track-whole', type: 'line', source: 'track-whole', paint: { 'line-color': css(container, '--color-series-navigation'), 'line-width': 2, 'line-opacity': 0.6, 'line-dasharray': [2.5, 2.5] } })
          m.addSource('track-flown', { type: 'geojson', data: lineString([]) })
          m.addLayer({ id: 'track-flown-glow', type: 'line', source: 'track-flown', paint: { 'line-color': nav, 'line-width': 8, 'line-opacity': 0.25, 'line-blur': 4 } })
          m.addLayer({ id: 'track-flown', type: 'line', source: 'track-flown', paint: { 'line-color': nav, 'line-width': 3 } })
          m.addSource('bearings', { type: 'geojson', data: empty })
          m.addLayer({ id: 'bearings', type: 'line', source: 'bearings', paint: { 'line-color': ['get', 'color'], 'line-width': 1.5, 'line-opacity': 0.85, 'line-dasharray': [3, 2.5] } })

          overlays = true
          paintFixes()
          last.position = -1 // force a redraw
          sweepShown = true
        }

        // HTML markers: start/end, missile alerts, estimated sites, aircraft.
        const start = Date.parse(startUtc)
        const endMarker = (i: number, label: string) => {
          const el = document.createElement('div')
          el.className = 'map-endpoint'
          el.title = `${label} ${formatUtcClock(new Date(start + track.t[i]))}`
          markers.push(new maplibregl.Marker({ element: el }).setLngLat(coords[i]).addTo(map!))
        }
        if (coords.length > 0) {
          endMarker(0, 'Start')
          endMarker(coords.length - 1, 'End')
        }
        // Missile alerts where the aircraft was when they sounded.
        for (const a of alerts) {
          const spec = ALERTS[a.kind]
          if (spec.severity !== 'critical') continue
          const p = positionAt(track, a.offsetMs)
          if (!p) continue
          const el = document.createElement('button')
          el.type = 'button'
          el.className = 'map-alert'
          el.title = `${spec.label} · ${formatElapsed(a.offsetMs)}${a.bearingDeg === null ? '' : ` · ${clockPosition(a.bearingDeg)}`}`
          el.setAttribute('aria-label', el.title)
          el.innerHTML = '<span aria-hidden="true"></span>'
          el.addEventListener('click', () => actor.send({ type: 'SEEK', positionMs: a.offsetMs }))
          markers.push(new maplibregl.Marker({ element: el }).setLngLat([p.lon, p.lat]).addTo(map!))
        }

        const resolved = fixes.filter((f) => f.resolved && f.lat !== null && f.lon !== null)
        const fixInfo = new Map<number, { color: string; range: number | undefined; lat: number; lon: number }>()
        const ruleFor = (f: EmitterFix, p: ResolvedProfile | undefined) =>
          p ? classify({ emitterId: f.emitterId, frequencyMHz: f.frequencyMHz, priUs: f.priUs, pulseWidthUs: f.pulseWidthUs }, p) : null

        // One marker per estimated site, created once; paintFixes classifies them (the profile may arrive later).
        const siteMarkers = resolved.map((f) => {
          const el = document.createElement('div')
          const diamond = document.createElement('span')
          diamond.className = 'map-site__diamond'
          diamond.setAttribute('aria-hidden', 'true')
          const label = document.createElement('span')
          label.className = 'map-site__label'
          el.append(diamond, label)
          // Anchor the diamond (not the label) on the site; MapLibre owns the marker element's transform.
          markers.push(new maplibregl.Marker({ element: el, anchor: 'left', offset: [-6, 0] }).setLngLat([f.lon!, f.lat!]).addTo(map!))
          return { el, label }
        })

        function paintFixes() {
          const current = live.current.profile
          const ellipses: GeoJSON.Feature[] = []
          const rings: GeoJSON.Feature[] = []
          fixInfo.clear()
          resolved.forEach((f, i) => {
            const rule = ruleFor(f, current)
            const lethality = rule?.lethality ?? 'NonLethal'
            const color = css(container, lethality === 'Critical' ? '--color-alert-critical' : lethality === 'Lethal' ? '--color-alert-warning' : '--color-muted-foreground')
            const range = rule ? NOMINAL_RANGE_M[rule.class] : undefined
            fixInfo.set(f.emitterId, { color, range, lat: f.lat!, lon: f.lon! })
            ellipses.push({ type: 'Feature', properties: { color }, geometry: { type: 'LineString', coordinates: ellipsePolygon(f.lat!, f.lon!, f.ellipseMajorM, f.ellipseMinorM, f.ellipseAzimuthDeg) } })
            if (range) rings.push({ type: 'Feature', properties: { color }, geometry: { type: 'LineString', coordinates: circlePolygon(f.lat!, f.lon!, range) } })

            const { el, label } = siteMarkers[i]
            el.className = `map-site map-site--${lethality.toLowerCase()}`
            label.textContent = `${rule?.code ?? f.emitterId} EST.`
            el.title = [
              `ESTIMATED site — ${rule?.code ?? `Emitter ${f.emitterId}`}${rule ? ` · ${rule.class} · ${rule.lethality}` : ''}`,
              `Estimated from ${f.bearings} bearings, ±${Math.round(f.ellipseMajorM)} × ${Math.round(f.ellipseMinorM)} m (95%)`,
              range ? `Nominal range ${range / 1000} km — assumed by class` : null,
            ]
              .filter(Boolean)
              .join('\n')
          })
          source('fix-ellipses')?.setData({ type: 'FeatureCollection', features: ellipses })
          source('fix-rings')?.setData({ type: 'FeatureCollection', features: rings })
        }

        const aircraftEl = document.createElement('div')
        aircraftEl.className = 'map-aircraft'
        aircraftEl.innerHTML = platform === 'Helicopter' ? HELICOPTER : FIXED_WING
        const aircraft = new maplibregl.Marker({ element: aircraftEl, rotationAlignment: 'map' }).setLngLat(coords[0] ?? [0, 0]).addTo(map)
        markers.push(aircraft)

        const themeObserver = new MutationObserver(() => {
          overlays = false
          // A full reload (not a diff), so style.load fires and the overlays are added again in the new colours.
          map?.setStyle(style.tacticalStyle(theme(), settings, tokens()), { diff: false })
        })
        themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
        observers.push(themeObserver)
        const resizeObserver = new ResizeObserver(() => map?.resize())
        resizeObserver.observe(container)
        observers.push(resizeObserver)

        const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
        let last = { position: -1, frames: null as unknown, profile: null as unknown, follow: false }
        let lastIndex = -2
        let flown: [number, number][] = []
        let lastReadout = Number.NEGATIVE_INFINITY
        let readoutStale = false
        let sweepShown = false
        handle = requestAnimationFrame(function draw(now) {
          handle = requestAnimationFrame(draw)
          if (!overlays) return
          const { positionMs, frames, speed } = actor.getSnapshot().context
          const { follow: following, profile: currentProfile } = live.current
          if (currentProfile !== last.profile) paintFixes()
          const changed = positionMs !== last.position || frames !== last.frames || currentProfile !== last.profile || following !== last.follow
          if (changed) {
            last = { position: positionMs, frames, profile: currentProfile, follow: following }
            readoutStale = true
            update(positionMs, frames, speed, currentProfile, following)
          }
          if (!reducedMotion && fixInfo.size > 0) sweep(now, positionMs, frames, speed, currentProfile)
          // Throttled, but never left stale: a paused playhead still gets its final values.
          if (readoutStale && now - lastReadout >= READOUT_EVERY_MS) {
            lastReadout = now
            readoutStale = false
            writeReadout(positionMs)
          }
        })

        function update(positionMs: number, frames: PlaybackFrame[], speed: number, currentProfile: ResolvedProfile | undefined, following: boolean) {
          const m = map!
          const p = positionAt(track, positionMs)
          if (!p) return
          const here: [number, number] = [p.lon, p.lat]
          const heading = headingAt(track, positionMs)
          // Flown part: the sample prefix is rebuilt only when the playhead passes a sample.
          const index = sampleIndex(track, positionMs)
          if (index !== lastIndex) {
            flown = coords.slice(0, index + 1)
            lastIndex = index
          }
          source('track-flown')?.setData(lineString([...flown, here]))
          aircraft.setLngLat(here).setRotation(heading ?? 0)

          // Lines of bearing to tracked or critical threats: true bearing = track + relative bearing
          // (track over the ground stands in for the heading the RWR measured against).
          const threats =
            currentProfile && heading !== null
              ? buildThreats(viewAt(frames, positionMs, stepForSpeed(speed)).emitters, currentProfile)
                  .filter((t) => t.locked || t.lethality === 'Critical')
                  .slice(0, MAX_BEARING_LINES)
              : []
          source('bearings')?.setData({
            type: 'FeatureCollection',
            features: threats.map((t) => {
              const [la, lo] = destination(p.lat, p.lon, (heading! + t.bearingDeg) % 360, BEARING_LINE_M)
              return { type: 'Feature', properties: { color: css(container, t.locked ? '--color-alert-critical' : '--color-alert-warning') }, geometry: { type: 'LineString', coordinates: [here, [lo, la]] } }
            }),
          })

          if (following) {
            if (live.current.mode === '3d') m.easeTo({ center: here, bearing: heading ?? m.getBearing(), duration: 0 })
            else {
              // Re-centre only when the aircraft nears the edge, so tiles are not reloaded every frame.
              const canvas = m.getCanvas()
              const pt = m.project(here)
              const w = canvas.clientWidth
              const h = canvas.clientHeight
              if (pt.x < w * 0.25 || pt.x > w * 0.75 || pt.y < h * 0.25 || pt.y > h * 0.75) m.setCenter(here)
            }
          }
        }

        /** A rotating wedge around each estimated site whose emitter is locked on the aircraft at the playhead. */
        function sweep(now: number, positionMs: number, frames: PlaybackFrame[], speed: number, currentProfile: ResolvedProfile | undefined) {
          const locked = currentProfile ? buildThreats(viewAt(frames, positionMs, stepForSpeed(speed)).emitters, currentProfile).filter((t) => t.locked && fixInfo.has(t.emitterId)) : []
          if (locked.length === 0 && !sweepShown) return
          const angle = ((now % SWEEP_PERIOD_MS) / SWEEP_PERIOD_MS) * 360
          const features: GeoJSON.Feature[] = locked.map((t) => {
            const info = fixInfo.get(t.emitterId)!
            return { type: 'Feature', properties: { color: info.color }, geometry: { type: 'Polygon', coordinates: [wedgePolygon(info.lat, info.lon, info.range ?? SWEEP_FALLBACK_M, angle, SWEEP_WIDTH_DEG)] } }
          })
          source('sweep')?.setData({ type: 'FeatureCollection', features })
          sweepShown = features.length > 0
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
      })
      .catch((error: unknown) => {
        console.warn('Map failed to start', error)
        if (!disposed) setFailed(true)
      })

    return () => {
      disposed = true
      cancelAnimationFrame(handle)
      observers.forEach((o) => o.disconnect())
      markers.forEach((m) => m.remove())
      map?.remove()
      mapRef.current = null
    }
  }, [actor, track, alerts, settings, startUtc, readout, platform, fixes, webgl])

  useEffect(() => {
    const map = mapRef.current
    const bounds = boundsRef.current
    if (fitRequest > 0 && map && bounds) map.fitBounds(bounds, { padding: 24, pitch: map.getPitch() })
  }, [fitRequest])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    map.easeTo({ pitch: mode === '3d' ? 60 : 0, duration: 600 })
  }, [mode])

  return (
    <div className="map-view">
      <div ref={containerRef} className="map-view__map" role="application" aria-label="Flight map: track, aircraft position, missile alerts and estimated emitter sites" />
      {(unavailable || settings.source === 'None') && (
        <div className="map-view__notes">
          {unavailable && <p className="map-view__note">The map needs WebGL, which this browser has turned off. The readouts below still follow the playhead.</p>}
          {settings.source === 'None' && <p className="map-view__note">No base map configured: track only.</p>}
        </div>
      )}
    </div>
  )
}
