import { createContext, useContext } from 'react'
import type { ActorRefFrom } from 'xstate'
import type { FlightDetail } from '../api/types'
import type { playbackMachine } from '../machines/playbackMachine'

export type PlaybackActor = ActorRefFrom<typeof playbackMachine>

export interface WorkspaceValue {
  flightId: string | null
  selectFlight: (id: string | null) => void
  flight: FlightDetail | undefined
  flightLoading: boolean
  /** RWR symbology chosen by the analyst; null = the flight customer's own profile. */
  symbologyId: string | null
  setSymbologyId: (id: string | null) => void
  /** Playback for the selected flight; null until it is a completed flight with events. */
  playback: { actor: PlaybackActor; durationMs: number; startUtc: string } | null
}

export const WorkspaceContext = createContext<WorkspaceValue | null>(null)

/** Shared workspace state. Panels anywhere in the dock (also floating or popped out) read it. */
export function useWorkspace(): WorkspaceValue {
  const value = useContext(WorkspaceContext)
  if (!value) throw new Error('useWorkspace must be used inside <WorkspaceProvider>.')
  return value
}

/** Playback runs from the recorded start to the last event. */
export function playbackDurationMs(f: FlightDetail): number {
  return f.lastEventUtc ? Math.max(0, Date.parse(f.lastEventUtc) - Date.parse(f.startUtc)) : 0
}
