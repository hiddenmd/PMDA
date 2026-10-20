import { DockviewReact, type DockviewApi, type DockviewReadyEvent, type DockviewTheme, type IDockviewHeaderActionsProps, type IDockviewPanelProps } from 'dockview-react'
import 'dockview-react/dist/styles/dockview.css'
import { ExternalLink, Maximize2, Minimize2, PictureInPicture2 } from '../components/icons'
import { useEffect, useRef, type FunctionComponent } from 'react'
import { GraphPanel } from '../features/graphs/GraphPanel'
import { AlertsPanel } from '../features/alerts/AlertsPanel'
import { MessagesPanel } from '../features/messages/MessagesPanel'
import { LAYOUT_PRESETS, type LayoutStore, type PanelKind } from './layouts'
import { CountermeasuresPanel } from './panels/CountermeasuresPanel'
import { EmittersPanel } from './panels/EmittersPanel'
import { FlightInfoPanel } from './panels/FlightInfoPanel'
import { FlightsPanel } from './panels/FlightsPanel'
import { AttitudePanel } from './panels/AttitudePanel'
import { MapPanel } from './panels/MapPanel'
import { ScopePanel } from './panels/ScopePanel'
import { UsersPanel } from './panels/UsersPanel'
import { DatabasesPanel } from './panels/DatabasesPanel'

const pmdaTheme: DockviewTheme = { name: 'pmda', className: 'dockview-theme-pmda', gap: 0, dndOverlayMounting: 'absolute', dndPanelOverlay: 'group' }

const components: Record<PanelKind, FunctionComponent<IDockviewPanelProps>> = {
  flights: FlightsPanel,
  info: FlightInfoPanel,
  scope: ScopePanel,
  map: MapPanel,
  attitude: AttitudePanel,
  emitters: EmittersPanel,
  countermeasures: CountermeasuresPanel,
  alerts: AlertsPanel,
  messages: MessagesPanel,
  graph: GraphPanel as FunctionComponent<IDockviewPanelProps>,
  users: UsersPanel,
  databases: DatabasesPanel,
}

interface DockProps {
  store: LayoutStore
  onReady: (api: DockviewApi) => void
}

/** The docking area. Restores the user's last layout (or the Debrief preset) and saves every change. */
export function Dock({ store, onReady }: DockProps) {
  const cleanupRef = useRef<() => void>(() => undefined)

  const handleReady = (event: DockviewReadyEvent) => {
    const api = event.api
    const saved = store.loadCurrent()
    let restored = false
    if (saved) {
      try {
        api.fromJSON(saved)
        restored = api.panels.length > 0
      } catch {
        store.clearCurrent() // a layout from an older version: start fresh
      }
    }
    if (!restored) LAYOUT_PRESETS[0].build(api)

    let timer: ReturnType<typeof setTimeout> | undefined
    const subscription = api.onDidLayoutChange(() => {
      clearTimeout(timer)
      timer = setTimeout(() => store.saveCurrent(api.toJSON()), 300)
    })
    cleanupRef.current = () => {
      clearTimeout(timer)
      subscription.dispose()
    }
    onReady(api)
  }

  useEffect(() => () => cleanupRef.current(), [])

  return (
    <div className="dock">
      <DockviewReact
        components={components}
        onReady={handleReady}
        theme={pmdaTheme}
        rightHeaderActionsComponent={GroupActions}
        popoutUrl="/popout.html"
      />
    </div>
  )
}

/** Per-group buttons: maximise, float, pop out to a new window. */
function GroupActions({ group, containerApi }: IDockviewHeaderActionsProps) {
  const maximised = group.api.isMaximized()
  const floating = group.api.location.type === 'floating'
  const popout = group.api.location.type === 'popout'

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 2, padding: '0 4px', height: '100%' }}>
      {!floating && !popout && (
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          aria-label={maximised ? 'Restore panel size' : 'Maximise panel'}
          title={maximised ? 'Restore' : 'Maximise'}
          onClick={() => (maximised ? group.api.exitMaximized() : group.api.maximize())}
        >
          {maximised ? <Minimize2 size={14} aria-hidden /> : <Maximize2 size={14} aria-hidden />}
        </button>
      )}
      {!floating && !popout && (
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          aria-label="Float panel"
          title="Float"
          onClick={() => containerApi.addFloatingGroup(group, { position: { top: 80, left: 120 }, width: 520, height: 380 })}
        >
          <PictureInPicture2 size={14} aria-hidden />
        </button>
      )}
      {!popout && (
        <button
          type="button"
          className="btn btn--ghost btn--icon btn--compact"
          aria-label="Open panel in a new window"
          title="Pop out to a new window"
          onClick={() => void containerApi.addPopoutGroup(group)}
        >
          <ExternalLink size={14} aria-hidden />
        </button>
      )}
    </div>
  )
}
