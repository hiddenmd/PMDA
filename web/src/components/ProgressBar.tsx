export function ProgressBar({ value, label }: { value: number; label: string }) {
  return (
    <div className="progress">
      <div className="progress__label">
        <span>{label}</span>
        <span>{value}%</span>
      </div>
      <div className="progress__track" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={value}>
        <div className="progress__fill" style={{ width: `${value}%` }} />
      </div>
    </div>
  )
}
