import { useEffect, useRef } from 'react'
import uPlot from 'uplot'
import 'uplot/dist/uPlot.min.css'

interface UPlotChartProps {
  /** Build options for a given size. Recreated (new chart) when this function's identity changes. */
  options: (width: number, height: number) => uPlot.Options
  data: uPlot.AlignedData
  onCreate?: (chart: uPlot) => void | (() => void)
  label: string
}

/**
 * Thin uPlot wrapper: one chart per options identity, new data via setData (no re-creation),
 * size follows the container.
 */
export function UPlotChart({ options, data, onCreate, label }: UPlotChartProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<uPlot | null>(null)
  const dataRef = useRef(data)
  const onCreateRef = useRef(onCreate)
  // Declared first, so a re-created chart starts from the latest data and callback.
  useEffect(() => {
    dataRef.current = data
    onCreateRef.current = onCreate
  })

  useEffect(() => {
    const el = containerRef.current!
    const { width, height } = el.getBoundingClientRect()
    const chart = new uPlot(options(Math.max(100, width), Math.max(80, height)), dataRef.current, el)
    chartRef.current = chart
    const cleanup = onCreateRef.current?.(chart)

    const observer = new ResizeObserver(([entry]) => {
      const r = entry.contentRect
      if (r.width > 0 && r.height > 0) chart.setSize({ width: r.width, height: r.height })
    })
    observer.observe(el)
    return () => {
      observer.disconnect()
      if (typeof cleanup === 'function') cleanup()
      chart.destroy()
      chartRef.current = null
    }
  }, [options])

  useEffect(() => {
    chartRef.current?.setData(data)
  }, [data])

  return <div ref={containerRef} role="img" aria-label={label} style={{ position: 'absolute', inset: 0 }} />
}
