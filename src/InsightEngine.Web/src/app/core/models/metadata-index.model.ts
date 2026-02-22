export type CorrelationMethod = 'Pearson' | 'Spearman' | 'CramersV' | 'EtaSquared' | 'MutualInformation';
export type CorrelationStrength = 'Low' | 'Medium' | 'High';
export type CorrelationDirection = 'Positive' | 'Negative' | 'None';
export type ConfidenceLevel = 'Low' | 'Medium' | 'High';
export type IndexBuildState = 'NotBuilt' | 'Building' | 'Ready' | 'Failed';
export type InferredType = 'Number' | 'Date' | 'Boolean' | 'Category' | 'String' | string;

export interface BuildIndexRequest {
  maxColumnsForCorrelation?: number;
  topKEdgesPerColumn?: number;
  sampleRows?: number;
  includeStringPatterns?: boolean;
  includeDistributions?: boolean;
}

export interface BuildIndexResponseData {
  datasetId: string;
  status: IndexBuildState;
  builtAt: string;
  limitsUsed: IndexLimits;
}

export interface DatasetIndexStatus {
  datasetId: string;
  status: IndexBuildState;
  updatedAtUtc: string;
  builtAtUtc?: string | null;
  message?: string | null;
  version: string;
}

export interface DatasetIndex {
  datasetId: string;
  builtAtUtc: string;
  version: string;
  schemaConfirmed?: boolean;
  targetColumn?: string | null;
  ignoredColumnsCount?: number;
  rowCount: number;
  columnCount: number;
  quality: DatasetQualityIndex;
  columns: ColumnIndex[];
  candidateKeys: KeyCandidate[];
  correlations: CorrelationIndex;
  tags: DatasetTag[];
  stats?: GlobalStatsIndex | null;
  limits: IndexLimits;
  formulaInference?: FormulaInferenceIndexEntry | null;
  targetFormulaSuggestion?: TargetFormulaSuggestionIndexEntry | null;
}

export interface FormulaInferenceIndexEntry {
  updatedAtUtc: string;
  result: FormulaInferenceResult;
}

export interface FormulaInferenceResult {
  status: 'NotRun' | 'Running' | 'Completed' | 'Failed';
  generatedAt: string;
  targetColumn: string;
  candidates: FormulaExpression[];
  numericCandidateColumns: string[];
  meta: Record<string, unknown>;
  warnings: string[];
}

export interface FormulaExpression {
  expressionText: string;
  targetColumn: string;
  usedColumns: string[];
  depth: number;
  operatorsUsed: string[];
  epsilonMaxAbsError: number;
  sampleRowsTested: number;
  rowsFailed: number;
  confidence: 'Low' | 'Medium' | 'High' | 'DeterministicLike';
  notes?: string | null;
}

export interface TargetFormulaSuggestionIndexEntry {
  bestCandidateExpressionText: string;
  confidence: 'Low' | 'Medium' | 'High' | 'DeterministicLike';
  usedColumns: string[];
}

export interface ColumnIndex {
  name: string;
  inferredType: InferredType;
  nullRate: number;
  distinctCount: number;
  numericStats?: NumericStatsIndex | null;
  dateStats?: DateStatsIndex | null;
  stringStats?: StringStatsIndex | null;
  topValues: string[];
  semanticTags: string[];
}

export interface NumericStatsIndex {
  mean?: number | null;
  stdDev?: number | null;
  min?: number | null;
  max?: number | null;
  p5?: number | null;
  p10?: number | null;
  p50?: number | null;
  p90?: number | null;
  p95?: number | null;
  histogram: HistogramBinIndex[];
}

export interface HistogramBinIndex {
  lowerBound: number;
  upperBound: number;
  count: number;
}

export interface DateStatsIndex {
  min?: string | null;
  max?: string | null;
  coverage: DateDensityBinIndex[];
  gaps: DateGapHintIndex[];
}

export interface DateDensityBinIndex {
  start: string;
  end: string;
  count: number;
}

export interface DateGapHintIndex {
  gapStart: string;
  gapEnd: string;
  approxMissingPeriods: number;
}

export interface StringStatsIndex {
  avgLength: number;
  minLength: number;
  maxLength: number;
  patternHints: string[];
}

export interface DatasetQualityIndex {
  duplicateRowRate: number;
  missingnessSummary: MissingnessSummaryIndex;
  parseIssuesCount: number;
  warnings: string[];
}

export interface MissingnessSummaryIndex {
  totalMissingValues: number;
  averageNullRate: number;
  medianNullRate: number;
  columnsWithNulls: number;
}

export interface KeyCandidate {
  columns: string[];
  uniquenessRatio: number;
  nullRate: number;
  confidence: ConfidenceLevel;
}

export interface CorrelationIndex {
  candidateColumnCount: number;
  edges: CorrelationEdge[];
}

export interface CorrelationEdge {
  leftColumn: string;
  rightColumn: string;
  method: CorrelationMethod;
  score: number;
  strength: CorrelationStrength;
  direction: CorrelationDirection;
  sampleSize: number;
  confidence: ConfidenceLevel;
}

export interface DatasetTag {
  name: string;
  source?: string | null;
  score: number;
}

export interface GlobalStatsIndex {
  numericColumnCount: number;
  dateColumnCount: number;
  categoryColumnCount: number;
  stringColumnCount: number;
  booleanColumnCount: number;
}

export interface IndexLimits {
  maxColumnsIndexed: number;
  maxColumnsForCorrelation: number;
  topKEdgesPerColumn: number;
  sampleRows: number;
  includeStringPatterns: boolean;
  includeDistributions: boolean;
}
