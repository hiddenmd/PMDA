import type { DockviewApi, SerializedDockview } from 'dockview-react'
import { DEFAULT_GRAPH, graphTitle, type GraphSettings } from '../features/graphs/graphKinds'
import { storage } from '../lib/preferences'

export type PanelKind = 'flights' | 'info' | 'scope' | 'map' | 'attitude' | 'emitters' | 'countermeasures' | 'alerts' | 'messages' | 'graph' | 'users'

interface PanelSpec {
  title: string
  /** Panels that can be open more than once get a unique id per instance. */
  multiple?: boolean
  /** Only offered to administrators (the API enforces it regardless). */
  adminOnly?: boolean
}

export const PANELS: Record<PanelKind, PanelSpec> = {
  flights: { title: 'Flights' },
  info: { title: 'Flight info' },
  scope: { title: 'RWR scope' },
  map: { title: 'Map' },
  attitude: { title: 'Attitude' },
  emitters: { title: 'Active emitters' },
  countermeasures: { title: 'Countermeasures' },
  alerts: { title: 'Audio alerts' },
  messages: { title: 'Message tables' },
  graph: { title: 'Graph', multiple: true },
  users: { title: 'Users', adminOnly: true },
}

function graphPanel(id: string, settings: GraphSettings) {
  return { id, component: 'graph', title: graphTitle(settings), params: settings }
}

/** Opens a panel (or focuses it if it is single-instance and already open) next to the active one. */
export function openPanel(api: DockviewApi, kind: PanelKind, params?: Record<string, unknown>) {
  const spec = PANELS[kind]
  if (!spec.multiple) {
    const existing = api.getPanel(kind)
    if (existing) {
      existing.api.setActive()
      return existing
    }
  }
  const id = spec.multiple ? `${kind}-${Date.now().toString(36)}` : kind
  const active = api.activeGroup
  const graphSettings = kind === 'graph' ? { ...DEFAULT_GRAPH, ...params } : undefined
  return api.addPanel({
    id,
    component: kind,
    title: graphSettings ? graphTitle(graphSettings) : spec.title,
    params: graphSettings ?? params,
    ...(active ? { position: { referenceGroup: active, direction: 'within' as const } } : {}),
  })
}

export interface LayoutPreset {
  id: string
  name: string
  build: (api: DockviewApi) => void
}

export const LAYOUT_PRESETS: LayoutPreset[] = [
  {
    id: 'debrief',
    name: 'Debrief',
    build(api) {
      api.clear()
      api.addPanel({ id: 'flights', component: 'flights', title: PANELS.flights.title, initialWidth: 320 })
      api.addPanel({ id: 'info', component: 'info', title: PANELS.info.title, position: { referencePanel: 'flights', direction: 'within' } })
      api.addPanel({ id: 'scope', component: 'scope', title: PANELS.scope.title, position: { referencePanel: 'flights', direction: 'right' } })
      api.addPanel({ id: 'emitters', component: 'emitters', title: PANELS.emitters.title, position: { referencePanel: 'scope', direction: 'right' } })
      api.addPanel({
        id: 'countermeasures',
        component: 'countermeasures',
        title: PANELS.countermeasures.title,
        position: { referencePanel: 'emitters', direction: 'below' },
      })
      api.addPanel({ id: 'alerts', component: 'alerts', title: PANELS.alerts.title, position: { referencePanel: 'countermeasures', direction: 'within' } })
      api.addPanel({ id: 'messages', component: 'messages', title: PANELS.messages.title, position: { referencePanel: 'scope', direction: 'below' } })
      api.addPanel({ id: 'map', component: 'map', title: PANELS.map.title, position: { referencePanel: 'messages', direction: 'within' } })
      api.addPanel({ id: 'attitude', component: 'attitude', title: PANELS.attitude.title, position: { referencePanel: 'map', direction: 'right' } })
      api.getPanel('map')?.api.setActive()
      api.getPanel('countermeasures')?.api.setActive()
      api.getPanel('flights')?.api.setActive()
    },
  },
  {
    id: 'analysis',
    name: 'Analysis',
    build(api) {
      api.clear()
      api.addPanel({ id: 'flights', component: 'flights', title: PANELS.flights.title, initialWidth: 300 })
      api.addPanel({ id: 'info', component: 'info', title: PANELS.info.title, position: { referencePanel: 'flights', direction: 'within' } })
      api.addPanel({ ...graphPanel('graph-bearing', { ...DEFAULT_GRAPH, kind: 'bearing' }), position: { referencePanel: 'flights', direction: 'right' } })
      api.addPanel({ ...graphPanel('graph-frequency', { ...DEFAULT_GRAPH, kind: 'parameter', param: 'Frequency' }), position: { referencePanel: 'graph-bearing', direction: 'below' } })
      // Absolute 'right': a full-height column at the edge, not a split of one graph.
      api.addPanel({ id: 'scope', component: 'scope', title: PANELS.scope.title, position: { direction: 'right' }, initialWidth: 380 })
      api.addPanel({ ...graphPanel('graph-scatter', { ...DEFAULT_GRAPH, kind: 'scatter', x: 'Frequency', y: 'Pri' }), position: { referencePanel: 'scope', direction: 'below' } })
      api.addPanel({ id: 'messages', component: 'messages', title: PANELS.messages.title, position: { referencePanel: 'graph-frequency', direction: 'below' } })
      api.addPanel({ ...graphPanel('graph-profile', { ...DEFAULT_GRAPH, kind: 'profile' }), position: { referencePanel: 'messages', direction: 'within' } })
      api.getPanel('flights')?.api.setActive()
    },
  },
]

/** Layouts are kept per user in this browser. */
export function layoutStore(userKey: string) {
  const currentKey = `pmda-layout:${userKey}:current`
  const namedKey = `pmda-layout:${userKey}:named`

  const readNamed = (): Record<string, SerializedDockview> => {
    try {
      return JSON.parse(storage.get(namedKey) ?? '{}') as Record<string, SerializedDockview>
    } catch {
      return {}
    }
  }

  return {
    saveCurrent: (layout: SerializedDockview) => storage.set(currentKey, JSON.stringify(layout)),
    loadCurrent(): SerializedDockview | null {
      try {
        const raw = storage.get(currentKey)
        return raw ? (JSON.parse(raw) as SerializedDockview) : null
      } catch {
        return null
      }
    },
    clearCurrent: () => storage.remove(currentKey),
    names: () => Object.keys(readNamed()).sort((a, b) => a.localeCompare(b)),
    load: (name: string) => readNamed()[name] ?? null,
    save(name: string, layout: SerializedDockview) {
      storage.set(namedKey, JSON.stringify({ ...readNamed(), [name]: layout }))
    },
    remove(name: string) {
      const named = readNamed()
      delete named[name]
      storage.set(namedKey, JSON.stringify(named))
    },
  }
}

export type LayoutStore = ReturnType<typeof layoutStore>
