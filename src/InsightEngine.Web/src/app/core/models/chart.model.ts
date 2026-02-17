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
  percentiles?: ChartPercentilesMeta;
  view?: ChartViewMeta;
}

export type PercentileKind = 'P5' | 'P10' | 'P90' | 'P95';
export type PercentileMode = 'None' | 'Bucket' | 'Overall' | 'NotApplicable';
export type ChartViewKind = 'Base' | 'Percentile';

export interface ChartPercentilesMeta {
  supported: boolean;
  mode: PercentileMode;
  available: PercentileKind[];
  reason?: string;
  values?: PercentileValue[];
}

export interface PercentileValue {
  kind: PercentileKind;
  value: number;
}

export interface ChartViewMeta {
  kind: ChartViewKind;
  percentileKind?: PercentileKind;
  percentileMode?: PercentileMode;
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
  scenarioMeta?: Record<string, unknown>;
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
  evidenceBytes?: number;
  outputBytes?: number;
  validationStatus?: string;
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

export interface AskAnalysisPlanResponse {
  intent: string;
  suggestedChartType: string;
  proposedDimensions: AskProposedDimensions;
  suggestedFilters: AskSuggestedFilter[];
  reasoning: string[];
  meta: AiGenerationMeta;
}

export interface AskProposedDimensions {
  x?: string | null;
  y?: string | null;
  groupBy?: string | null;
}

export interface AskSuggestedFilter {
  column: string;
  operator: string;
  values: string[];
}

export interface DeepInsightsRequest extends AiSummaryRequest {
  scenario?: ScenarioSimulationRequest;
  horizon?: number;
  sensitiveMode?: boolean;
  includeEvidence?: boolean;
}

export interface DeepInsightsResponse {
  report: DeepInsightReport;
  meta: AiGenerationMeta;
  explainability: DeepInsightsExplainability;
  evidencePack?: EvidencePack;
}

export interface DeepInsightsExplainability {
  evidenceUsedCount: number;
  topEvidenceIdsUsed: string[];
}

export interface DeepInsightReport {
  headline: string;
  executiveSummary: string;
  keyFindings: DeepInsightFinding[];
  drivers: DeepInsightDriver[];
  risksAndCaveats: DeepInsightRisk[];
  projections: DeepInsightProjections;
  recommendedActions: DeepInsightAction[];
  nextQuestions: string[];
  citations: DeepInsightCitation[];
  meta: DeepInsightMeta;
}

export interface DeepInsightFinding {
  title: string;
  narrative: string;
  evidenceIds: string[];
  severity: 'low' | 'medium' | 'high';
}

export interface DeepInsightDriver {
  driver: string;
  whyItMatters: string;
  evidenceIds: string[];
}

export interface DeepInsightRisk {
  risk: string;
  mitigation: string;
  evidenceIds: string[];
}

export interface DeepInsightProjections {
  horizon: string;
  methods: DeepInsightProjectionMethod[];
  conclusion: string;
}

export interface DeepInsightProjectionMethod {
  method: 'naive' | 'movingAverage' | 'linearRegression';
  narrative: string;
  confidence: 'low' | 'medium' | 'high';
  evidenceIds: string[];
}

export interface DeepInsightAction {
  action: string;
  expectedImpact: string;
  effort: 'low' | 'medium' | 'high';
  evidenceIds: string[];
}

export interface DeepInsightCitation {
  evidenceId: string;
  shortClaim: string;
}

export interface DeepInsightMeta {
  provider: string;
  model: string;
  promptVersion: string;
  evidenceVersion: string;
}

export interface EvidencePack {
  evidenceVersion: string;
  datasetId: string;
  recommendationId: string;
  queryHash: string;
  serializedBytes: number;
  truncated: boolean;
  facts: EvidenceFact[];
}

export interface EvidenceFact {
  evidenceId: string;
  shortClaim: string;
  value: string;
}

export type TrendSignal = 'Up' | 'Down' | 'Flat';
export type VolatilitySignal = 'Low' | 'Medium' | 'High';
export type OutlierSignal = 'None' | 'Few' | 'Many';
export type SeasonalitySignal = 'None' | 'Weak' | 'Strong';
