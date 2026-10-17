import { useEffect, useId, useRef, useState, type ReactNode } from 'react'

interface MenuProps {
  /** Content of the trigger button. */
  trigger: ReactNode
  /** Accessible name of the trigger. */
  label: string
  align?: 'left' | 'right'
  triggerClassName?: string
  children: (close: () => void) => ReactNode
}

/**
 * Button + popover menu. Esc and clicks outside close it; ↑/↓ move between items; focus returns
 * to the trigger on close.
 */
export function Menu({ trigger, label, align = 'right', triggerClassName = 'btn btn--ghost btn--compact', children }: MenuProps) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  const close = () => setOpen(false)

  // Return focus to the trigger whenever an open menu closes.
  // (Not after a click elsewhere: that click decides where focus goes.)
  const wasOpen = useRef(false)
  const closedByOutsideClick = useRef(false)
  useEffect(() => {
    if (wasOpen.current && !open && !closedByOutsideClick.current) buttonRef.current?.focus()
    wasOpen.current = open
    closedByOutsideClick.current = false
  }, [open])

  useEffect(() => {
    if (!open) return
    const first = rootRef.current?.querySelector<HTMLElement>('[role="menuitem"], [role="menuitemradio"], button, input')
    first?.focus()
    const onPointerDown = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) {
        closedByOutsideClick.current = true
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (!open) return
    if (e.key === 'Escape') {
      e.preventDefault()
      close()
      return
    }
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
    const items = Array.from(rootRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"], [role="menuitemradio"]') ?? [])
    if (items.length === 0) return
    e.preventDefault()
    const index = items.indexOf(document.activeElement as HTMLElement)
    const next = e.key === 'ArrowDown' ? (index + 1) % items.length : (index - 1 + items.length) % items.length
    items[next].focus()
  }

  return (
    <div className="menu-anchor" ref={rootRef} onKeyDown={onKeyDown}>
      <button
        ref={buttonRef}
        type="button"
        className={triggerClassName}
        aria-label={label}
        title={label}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((o) => !o)}
      >
        {trigger}
      </button>
      {open && (
        <div id={menuId} role="menu" aria-label={label} className={`menu${align === 'left' ? ' menu--left' : ''}`}>
          {children(close)}
        </div>
      )}
    </div>
  )
}

interface MenuItemProps {
  icon?: ReactNode
  children: ReactNode
  end?: ReactNode
  danger?: boolean
  disabled?: boolean
  onSelect: () => void
}

export function MenuItem({ icon, children, end, danger, disabled, onSelect }: MenuItemProps) {
  return (
    <button type="button" role="menuitem" className={`menu__item${danger ? ' menu__item--danger' : ''}`} disabled={disabled} onClick={onSelect}>
      {icon}
      <span>{children}</span>
      {end && <span className="menu__item-end">{end}</span>}
    </button>
  )
}

export const MenuSeparator = () => <div className="menu__separator" role="separator" />

export const MenuLabel = ({ children }: { children: ReactNode }) => <div className="menu__label label">{children}</div>
