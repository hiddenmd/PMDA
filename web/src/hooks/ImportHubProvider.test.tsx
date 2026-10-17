import { render, screen } from '@testing-library/react'
import { StrictMode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type { ImportHub } from '../api/importHub'
import { ImportHubProvider } from './ImportHubProvider'
import { useImportHub } from './useImportHub'

function fakeHub() {
  let connected = false
  return {
    start: vi.fn(async () => {
      // Like SignalR: starting a connection that is not disconnected fails.
      if (connected) throw new Error("Cannot start a HubConnection that is not in the 'Disconnected' state.")
      connected = true
    }),
    stop: vi.fn(async () => {
      connected = false
    }),
    onImportUpdated: () => () => undefined,
    onReconnecting: () => () => undefined,
    onReconnected: () => () => undefined,
    onClose: () => () => undefined,
  } satisfies ImportHub
}

function Status() {
  return <p>{useImportHub().status}</p>
}

describe('ImportHubProvider', () => {
  it('connects under StrictMode double mounting and stops the hub on unmount', async () => {
    const hub = fakeHub()
    const { unmount } = render(
      <StrictMode>
        <ImportHubProvider hub={hub}>
          <Status />
        </ImportHubProvider>
      </StrictMode>,
    )

    expect(await screen.findByText('connected')).toBeInTheDocument()
    // The simulated unmount stopped the first connection before the second start.
    expect(hub.stop).toHaveBeenCalledTimes(1)
    expect(hub.start).toHaveBeenCalledTimes(2)

    unmount()
    expect(hub.stop).toHaveBeenCalledTimes(2)
  })
})
