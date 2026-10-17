import { useCallback, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react'
import { createActor } from 'xstate'
import { useFlight } from '../hooks/useFlights'
import { playbackMachine } from '../machines/playbackMachine'
import { usePlaybackShortcuts } from '../features/playback/usePlayback'
import { playbackDurationMs, WorkspaceContext, type PlaybackActor, type WorkspaceValue } from './workspaceContext'

const FLIGHT_PARAM = 'flight'

/** Holds the current playback actor; React reads it with useSyncExternalStore. */
function createPlaybackStore() {
  let actor: PlaybackActor | null = null
  const listeners = new Set<() => void>()
  const emit = () => listeners.forEach((l) => l())
  return {
    get: () => actor,
    subscribe: (listener: () => void) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    open(flightId: string, durationMs: number) {
      const next = createActor(playbackMachine, { input: { flightId, durationMs } }).start()
      actor = next
      emit()
      return () => {
        next.stop()
        if (actor === next) {
          actor = null
          emit()
        }
      }
    },
  }
}

function flightFromUrl(): string | null {
  return new URLSearchParams(window.location.search).get(FLIGHT_PARAM)
}

/**
 * Owns the selected flight (mirrored in the URL so a view can be shared or reloaded) and one
 * playback machine per playable flight. The actor is created and stopped in an effect, so a flight
 * switch or unmount always stops the old machine and its timers/requests.
 */
export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [flightId, setFlightId] = useState<string | null>(flightFromUrl)
  const flight = useFlight(flightId)
  const [store] = useState(createPlaybackStore)
  const actor = useSyncExternalStore(store.subscribe, store.get)

  const selectFlight = useCallback((id: string | null) => {
    setFlightId(id)
    const url = new URL(window.location.href)
    if (id) url.searchParams.set(FLIGHT_PARAM, id)
    else url.searchParams.delete(FLIGHT_PARAM)
    window.history.replaceState(null, '', url)
  }, [])

  const playable = flight.data && flight.data.id === flightId && flight.data.status === 'Completed' ? flight.data : undefined
  const durationMs = playable ? playbackDurationMs(playable) : 0

  const playableId = playable?.id
  useEffect(() => {
    if (!playableId || durationMs <= 0) return
    return store.open(playableId, durationMs)
  }, [store, playableId, durationMs])

  const send = useCallback((e: Parameters<PlaybackActor['send']>[0]) => actor?.send(e), [actor])
  const currentSpeed = useCallback(() => actor?.getSnapshot().context.speed ?? 1, [actor])
  usePlaybackShortcuts(send, currentSpeed)

  const value = useMemo<WorkspaceValue>(
    () => ({
      flightId,
      selectFlight,
      flight: flight.data && flight.data.id === flightId ? flight.data : undefined,
      flightLoading: flightId !== null && flight.isPending,
      playback: actor && playable ? { actor, durationMs, startUtc: playable.startUtc } : null,
    }),
    [flightId, selectFlight, flight.data, flight.isPending, actor, playable, durationMs],
  )

  return <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>
}
