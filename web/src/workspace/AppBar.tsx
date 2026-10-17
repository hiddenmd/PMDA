import type { DockviewApi } from 'dockview-react'
import { Check, LayoutTemplate, PanelsTopLeft, Plus, Radar, RotateCcw, Save, Settings2, Trash2 } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { ConnectionBadge } from '../components/ConnectionBadge'
import { Dialog } from '../components/Dialog'
import { Menu, MenuItem, MenuLabel, MenuSeparator } from '../components/Menu'
import { StatusChip } from '../components/StatusChip'
import { useImportHub } from '../hooks/useImportHub'
import { applyTheme, applyUiSize, initialTheme, initialUiSize, UI_SIZES, type Theme, type UiSize } from '../lib/preferences'
import { LAYOUT_PRESETS, openPanel, PANELS, type LayoutStore, type PanelKind } from './layouts'
import { useWorkspace } from './workspaceContext'

interface AppBarProps {
  api: DockviewApi | null
  store: LayoutStore
  /** User controls, supplied by the authenticated shell. */
  userMenu?: ReactNode
}

export function AppBar({ api, store, userMenu }: AppBarProps) {
  const { status } = useImportHub()
  const { flight } = useWorkspace()

  return (
    <header className="appbar">
      <div className="brand">
        <Radar size={18} aria-hidden />
        <h1>PMDA</h1>
      </div>
      {flight && (
        <>
          <span className="appbar__divider" aria-hidden />
          <div className="appbar__flight" aria-label="Selected flight">
            <strong>{flight.tail}</strong>
            <span className="muted">
              {flight.missionId} · {flight.customerId}
            </span>
            <StatusChip status={flight.status} />
          </div>
        </>
      )}
      <div className="appbar__spacer" />
      <ConnectionBadge status={status} />
      <PanelsMenu api={api} />
      <LayoutsMenu api={api} store={store} />
      <SettingsMenu />
      {userMenu}
    </header>
  )
}

function PanelsMenu({ api }: { api: DockviewApi | null }) {
  return (
    <Menu
      label="Add panel"
      trigger={
        <>
          <PanelsTopLeft size={16} aria-hidden /> Panels
        </>
      }
    >
      {(close) =>
        (Object.keys(PANELS) as PanelKind[]).map((kind) => (
          <MenuItem
            key={kind}
            icon={<Plus size={14} aria-hidden />}
            end={PANELS[kind].multiple ? 'new' : undefined}
            disabled={!api}
            onSelect={() => {
              if (api) openPanel(api, kind)
              close()
            }}
          >
            {PANELS[kind].title}
          </MenuItem>
        ))
      }
    </Menu>
  )
}

function LayoutsMenu({ api, store }: { api: DockviewApi | null; store: LayoutStore }) {
  const [saving, setSaving] = useState(false)
  const [name, setName] = useState('')
  const [, refresh] = useState(0)
  const names = store.names()

  const save = () => {
    const trimmed = name.trim()
    if (!api || !trimmed) return
    store.save(trimmed, api.toJSON())
    setSaving(false)
    setName('')
    refresh((n) => n + 1)
  }

  return (
    <>
      <Menu
        label="Layouts"
        trigger={
          <>
            <LayoutTemplate size={16} aria-hidden /> Layouts
          </>
        }
      >
        {(close) => (
          <>
            <MenuLabel>Presets</MenuLabel>
            {LAYOUT_PRESETS.map((preset) => (
              <MenuItem
                key={preset.id}
                icon={<LayoutTemplate size={14} aria-hidden />}
                disabled={!api}
                onSelect={() => {
                  if (api) preset.build(api)
                  close()
                }}
              >
                {preset.name}
              </MenuItem>
            ))}
            {names.length > 0 && (
              <>
                <MenuSeparator />
                <MenuLabel>Saved</MenuLabel>
                {names.map((n) => (
                  <div key={n} style={{ display: 'flex' }}>
                    <MenuItem
                      icon={<Check size={14} aria-hidden style={{ visibility: 'hidden' }} />}
                      disabled={!api}
                      onSelect={() => {
                        const layout = store.load(n)
                        if (api && layout) api.fromJSON(layout)
                        close()
                      }}
                    >
                      {n}
                    </MenuItem>
                    <button
                      type="button"
                      className="btn btn--ghost btn--icon btn--compact"
                      aria-label={`Delete layout ${n}`}
                      title="Delete layout"
                      onClick={() => {
                        store.remove(n)
                        refresh((x) => x + 1)
                      }}
                    >
                      <Trash2 size={14} aria-hidden />
                    </button>
                  </div>
                ))}
              </>
            )}
            <MenuSeparator />
            <MenuItem
              icon={<Save size={14} aria-hidden />}
              disabled={!api}
              onSelect={() => {
                close()
                setSaving(true)
              }}
            >
              Save layout as…
            </MenuItem>
            <MenuItem
              icon={<RotateCcw size={14} aria-hidden />}
              disabled={!api}
              onSelect={() => {
                if (api) LAYOUT_PRESETS[0].build(api)
                close()
              }}
            >
              Reset layout
            </MenuItem>
          </>
        )}
      </Menu>

      <Dialog open={saving} title="Save layout" icon={<Save size={18} aria-hidden />} onClose={() => setSaving(false)}>
        <form
          className="dialog__body"
          style={{ padding: 0 }}
          onSubmit={(e) => {
            e.preventDefault()
            save()
          }}
        >
          <label className="field">
            <span>Layout name</span>
            <input className="input" autoFocus value={name} onChange={(e) => setName(e.target.value)} maxLength={40} placeholder="e.g. Threat analysis" />
          </label>
          {names.includes(name.trim()) && <p className="small muted">A layout with this name exists and will be replaced.</p>}
          <div className="dialog__actions">
            <button type="button" className="btn btn--secondary" onClick={() => setSaving(false)}>
              Cancel
            </button>
            <button type="submit" className="btn btn--primary" disabled={!name.trim()}>
              Save
            </button>
          </div>
        </form>
      </Dialog>
    </>
  )
}

function SettingsMenu() {
  const [theme, setTheme] = useState<Theme>(initialTheme)
  const [size, setSize] = useState<UiSize>(initialUiSize)

  return (
    <Menu label="Settings" triggerClassName="btn btn--ghost btn--icon btn--compact" trigger={<Settings2 size={16} aria-hidden />}>
      {() => (
        <>
          <div className="menu__row">
            <span>Theme</span>
            <div className="segmented" role="group" aria-label="Theme">
              {(['dark', 'light'] as const).map((t) => (
                <button
                  key={t}
                  type="button"
                  aria-pressed={theme === t}
                  onClick={() => {
                    applyTheme(t, true)
                    setTheme(t)
                  }}
                >
                  {t === 'dark' ? 'Dark' : 'Light'}
                </button>
              ))}
            </div>
          </div>
          <div className="menu__row">
            <span>UI size</span>
            <div className="segmented" role="group" aria-label="UI size">
              {UI_SIZES.map((s) => (
                <button
                  key={s.value}
                  type="button"
                  aria-pressed={size === s.value}
                  onClick={() => {
                    applyUiSize(s.value, true)
                    setSize(s.value)
                  }}
                >
                  {s.label}
                </button>
              ))}
            </div>
          </div>
          <MenuSeparator />
          <p className="menu__label small muted" style={{ textTransform: 'none', letterSpacing: 0, fontWeight: 400 }}>
            Shortcuts: Space play/pause · ←/→ step · [ ] speed
          </p>
        </>
      )}
    </Menu>
  )
}
