export interface ChartResponse {
  datasetId: string;
  recommendationId: string;
  option: any; // ECharts option object
  meta?: ChartMeta;
  insightSummary?: InsightSummary;
}

export interface ScenarioSimulationRequest {
  targetMetric: string;
  targetDimension: string;
  aggregation?: string;
  operations: ScenarioOperationRequest[];
  filters?: ScenarioFilterRequest[];
}

export interface ScenarioOperationRequest {
  type: ScenarioOperationType;
  column?: string;
  values?: string[];
  factor?: number;
  constant?: number;
  min?: number;
  max?: number;
}

export interface ScenarioFilterRequest {
  column: string;
  operator: string;
  values: string[];
}

export type ScenarioOperationType =
  | 'RemoveCategory'
  | 'MultiplyMetric'
  | 'AddConstant'
  | 'Clamp'
  | 'FilterOut';

export interface ScenarioSimulationResponse {
  datasetId: string;
  targetMetric: string;
  targetDimension: string;
  queryHash: string;
  rowCountReturned: number;
  duckDbMs: number;
  baselineSeries: ScenarioSeriesPoint[];
  simulatedSeries: ScenarioSeriesPoint[];
  deltaSeries: ScenarioDeltaPoint[];
  deltaSummary: ScenarioDeltaSummary;
}

export interface ScenarioSeriesPoint {
  dimension: string;
  value: number;
}

export interface ScenarioDeltaPoint {
  dimension: string;
  baseline: number;
  simulated: number;
  delta: number;
  deltaPercent?: number;
}

export interface ScenarioDeltaSummary {
  averageDeltaPercent: number;
  maxDeltaPercent: number;
  minDeltaPercent: number;
  changedPoints: number;
}

export interface ChartMeta {
  rowCountReturned: number;
  executionMs?: number;
  chartType?: string;
  generatedAt?: string;
  queryHash?: string;
  cacheHit?: boolean;
}

export interface InsightSummary {
  headline: string;
  bulletPoints: string[];
  signals: InsightSignals;
  confidence: number;
}

export interface InsightSignals {
  trend: TrendSignal;
  volatility: VolatilitySignal;
  outliers: OutlierSignal;
  seasonality: SeasonalitySignal;
}

export interface AiSummaryRequest {
  aggregation?: string;
  timeBin?: string;
  metricY?: string;
  groupBy?: string;
  filters?: string[];
}

export interface AiSummaryResponse {
  insightSummary: AiInsightSummary;
  meta: AiGenerationMeta;
}

export interface AiInsightSummary {
  headline: string;
  bulletPoints: string[];
  cautions: string[];
  nextQuestions: string[];
  confidence: number;
}

export interface AiGenerationMeta {
  provider: string;
  model: string;
  durationMs: number;
  cacheHit: boolean;
  fallbackUsed: boolean;
  fallbackReason?: string;
}

export interface ExplainChartResponse {
  explanation: string[];
  keyTakeaways: string[];
  potentialCauses: string[];
  caveats: string[];
  suggestedNextSteps: string[];
  questionsToAsk: string[];
  meta: AiGenerationMeta;
}

export type TrendSignal = 'Up' | 'Down' | 'Flat';
export type VolatilitySignal = 'Low' | 'Medium' | 'High';
export type OutlierSignal = 'None' | 'Few' | 'Many';
export type SeasonalitySignal = 'None' | 'Weak' | 'Strong';
