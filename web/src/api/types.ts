import type { components } from './schema'

type Schemas = components['schemas']

export type ImportJob = Schemas['ImportJobDto']
export type ImportState = Schemas['ImportState']
export type FlightSummary = Schemas['FlightSummaryDto']
export type FlightDetail = Schemas['FlightDetailDto']
export type FlightPage = Schemas['FlightPageDto']
export type FlightStatus = Schemas['FlightStatus']
export type Diagnostic = Schemas['DiagnosticDto']
export type ProblemDetails = Schemas['ProblemDetails']

export const finalImportStates: readonly ImportState[] = ['Completed', 'Duplicate', 'Failed', 'Cancelled']

export const isFinalImportState = (state: ImportState) => finalImportStates.includes(state)
