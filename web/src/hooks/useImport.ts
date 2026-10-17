import { useQueryClient } from '@tanstack/react-query'
import { useActorRef, useSelector } from '@xstate/react'
import { useEffect, useMemo } from 'react'
import type { ImportJob } from '../api/types'
import { importMachine, importProgress, importStatus, type ImportStatus } from '../machines/importMachine'
import { useImportHub } from './useImportHub'
import { flightKeys } from './useFlights'

export interface ImportViewModel {
  status: ImportStatus
  file: File | null
  job: ImportJob | null
  error: string | null
  uploadPercent: number
  processPercent: number
  start: (file: File) => void
  cancel: () => void
  retry: () => void
  reset: () => void
}

/** One import, driven by importMachine and fed by the shared hub connection. */
export function useImport(): ImportViewModel {
  const { hub, status: hubStatus } = useImportHub()
  const queryClient = useQueryClient()
  const hubConnected = hubStatus === 'connected'

  const actor = useActorRef(importMachine, { input: { hubConnected } })
  const snapshot = useSelector(actor, (s) => s)

  useEffect(() => {
    actor.send({ type: 'HUB_STATUS', connected: hubConnected })
  }, [actor, hubConnected])

  useEffect(() => hub.onImportUpdated((job) => actor.send({ type: 'JOB_UPDATED', job })), [actor, hub])

  const status = importStatus(snapshot)
  useEffect(() => {
    // Any finished import may have added or changed a flight; covers the polling path too,
    // where no hub event announces it.
    if (status === 'completed' || status === 'failed' || status === 'cancelled') {
      void queryClient.invalidateQueries({ queryKey: flightKeys.all })
    }
  }, [status, queryClient])

  const actions = useMemo(
    () => ({
      start: (file: File) => actor.send({ type: 'START', file }),
      cancel: () => actor.send({ type: 'CANCEL' }),
      retry: () => actor.send({ type: 'RETRY' }),
      reset: () => actor.send({ type: 'RESET' }),
    }),
    [actor],
  )

  const { context } = snapshot
  return {
    status,
    file: context.file,
    job: context.job,
    error: context.error,
    ...importProgress(context),
    ...actions,
  }
}
