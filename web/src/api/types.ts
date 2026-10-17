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

export type EventType = Schemas['EventType']
export type PlaybackFrame = Schemas['PlaybackFrameDto']
export type PlaybackFrames = Schemas['PlaybackFramesDto']
export type EmitterHit = Schemas['EmitterHitDto']
export type Position = Schemas['PositionDto']
export type FlightOverview = Schemas['FlightOverviewDto']

export type EventRow = Schemas['EventRowDto']
export type EventPage = Schemas['EventPageDto']
export type SeriesParam = Schemas['SeriesParam']
export type ParameterSeries = Schemas['ParameterSeriesDto']
export type ScatterSeries = Schemas['ScatterSeriesDto']
export type ProfileSeries = Schemas['ProfileSeriesDto']
export type RateSeries = Schemas['RateSeriesDto']
export type FlightSort = NonNullable<import('./flights').FlightListParams['sort']>
