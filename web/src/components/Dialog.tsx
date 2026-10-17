import { useEffect, useRef, type ReactNode } from 'react'

interface DialogProps {
  open: boolean
  title: ReactNode
  icon?: ReactNode
  onClose: () => void
  children: ReactNode
}

/**
 * Modal dialog on the native <dialog> element: focus trapping, Esc to close and the backdrop come
 * from the browser.
 */
export function Dialog({ open, title, icon, onClose, children }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal?.()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog ref={ref} className="dialog" aria-labelledby="dialog-title" onClose={onClose} onCancel={onClose}>
      {open && (
        <div className="dialog__body">
          <div className="dialog__title">
            {icon}
            <h2 id="dialog-title">{title}</h2>
          </div>
          {children}
        </div>
      )}
    </dialog>
  )
}
