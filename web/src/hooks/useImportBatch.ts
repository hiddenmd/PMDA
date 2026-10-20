import { useQueryClient } from '@tanstack/react-query'
import { useActorRef, useSelector } from '@xstate/react'
import { useEffect, useMemo } from 'react'
import type { ActorRefFrom } from 'xstate'
import { batchSummary, importBatchMachine, type BatchSummary } from '../machines/importBatchMachine'
import { flightKeys } from './useFlights'
import { useImportHub } from './useImportHub'

const sameSummary = (a: BatchSummary, b: BatchSummary) => (Object.keys(a) as (keyof BatchSummary)[]).every((k) => a[k] === b[k])

/** A batch of file imports, driven by importBatchMachine and fed by the shared hub connection. */
export function useImportBatch() {
  const { hub, status: hubStatus } = useImportHub()
  const queryClient = useQueryClient()
  const hubConnected = hubStatus === 'connected'

  const actor = useActorRef(importBatchMachine, { input: { hubConnected } })
  const batch = useBatchView(actor)
  const { summary } = batch

  useEffect(() => {
    actor.send({ type: 'HUB_STATUS', connected: hubConnected })
  }, [actor, hubConnected])

  useEffect(() => hub.onImportUpdated((job) => actor.send({ type: 'JOB_UPDATED', job })), [actor, hub])

  // Each finished file may have added or changed a flight; covers the polling path too, where no hub event announces it.
  const finished = summary.completed + summary.failed + summary.cancelled
  useEffect(() => {
    if (finished > 0) void queryClient.invalidateQueries({ queryKey: flightKeys.all })
  }, [finished, queryClient])

  return batch
}

export type ImportBatchActor = ActorRefFrom<typeof importBatchMachine>

/** Files, counts and commands of a batch actor; no I/O of its own (tests drive it with fake uploads). */
export function useBatchView(actor: ImportBatchActor) {
  const items = useSelector(actor, (s) => s.context.items)
  const summary = useSelector(actor, batchSummary, sameSummary)
  const actions = useMemo(
    () => ({
      add: (files: File[], databaseId?: string | null, format?: string | null) => {
        if (files.length > 0) actor.send({ type: 'ADD_FILES', files, databaseId, format })
      },
      cancel: (id: string) => actor.send({ type: 'CANCEL', id }),
      cancelAll: () => actor.send({ type: 'CANCEL_ALL' }),
      retry: (id: string) => actor.send({ type: 'RETRY', id }),
      remove: (id: string) => actor.send({ type: 'REMOVE', id }),
      clearFinished: () => actor.send({ type: 'CLEAR_FINISHED' }),
    }),
    [actor],
  )

  return { actor, items, summary, ...actions }
}

export type ImportBatch = ReturnType<typeof useBatchView>
