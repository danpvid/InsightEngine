export interface UploadDatasetResponse {
  datasetId: string;
  fileName?: string;
  rowCount?: number;
  columnCount?: number;
  uploadedAt?: string;
}

export interface DataSetSummary {
  datasetId: string;
  originalFileName: string;
  storedFileName: string;
  fileSizeInBytes: number;
  fileSizeMB: number;
  createdAt: string;
  lastAccessedAt?: string;
  rowCount?: number;
  profileSummary?: string | null;
}

export interface DataSetDeletionResponse {
  datasetId: string;
  removedMetadataRecord: boolean;
  deletedFile: boolean;
  deletedLegacyArtifacts: boolean;
  clearedMetadataCache: boolean;
  clearedChartCache: boolean;
}

export interface DatasetProfile {
  datasetId: string;
  rowCount: number;
  sampleSize: number;
  targetColumn?: string | null;
  ignoredColumns?: string[];
  schemaConfirmed?: boolean;
  columns: DatasetColumnProfile[];
}

export interface DatasetColumnProfile {
  name: string;
  inferredType: string;
  confirmedType?: string;
  isIgnored?: boolean;
  isTarget?: boolean;
  currencyCode?: string | null;
  hasPercentSign?: boolean;
  nullRate: number;
  distinctCount: number;
  topValues: string[];
  topValueStats?: DatasetColumnTopValueStat[];
}

export interface ImportPreviewHints {
  hasPercentSign: boolean;
  hasCurrencySymbol: boolean;
  mostlyZeroToOne: boolean;
  mostlyZeroToHundred: boolean;
  mostlyInteger: boolean;
  consistentTwoDecimalPlaces: boolean;
  currencyCode: string;
}

export interface ImportPreviewColumn {
  name: string;
  inferredType: string;
  confidence: number;
  reasons: string[];
  hints: ImportPreviewHints;
}

export interface ImportPreviewResponse {
  tempUploadId: string;
  sampleSize: number;
  columns: ImportPreviewColumn[];
  sampleRows: Array<Record<string, string>>;
  suggestedTargetCandidates: string[];
  suggestedIgnoredCandidates: string[];
  suggestedUniqueKeyCandidates: string[];
}

export interface FinalizeImportRequest {
  importMode?: 'standard' | 'with-index';
  targetColumn?: string | null;
  uniqueKeyColumn?: string | null;
  ignoredColumns: string[];
  columnTypeOverrides: Record<string, string>;
  currencyCode: string;
}

export interface FinalizeImportResponse {
  datasetId: string;
  schemaVersion: number;
  targetColumn?: string | null;
  uniqueKeyColumn?: string | null;
  ignoredColumnsCount: number;
  storedColumnsCount: number;
  currencyCode: string;
}

export interface DatasetColumnTopValueStat {
  value: string;
  count: number;
}

export interface RawDatasetRowsResponse {
  datasetId: string;
  columns: string[];
  rowCountTotal: number;
  rowCountReturned: number;
  page: number;
  pageSize: number;
  totalPages: number;
  truncated: boolean;
  fieldStats?: RawFieldStats | null;
  rows: RawDatasetRow[];
}

export interface RawDatasetRow {
  [key: string]: string | null;
}

export interface RawDistinctValueStat {
  value: string;
  count: number;
}

export interface RawRangeValueStat {
  label: string;
  from: string;
  to: string;
  count: number;
}

export interface RawFieldStats {
  column: string;
  inferredType: string;
  distinctCount: number;
  nullCount: number;
  topValues: RawDistinctValueStat[];
  topRanges: RawRangeValueStat[];
}

export type FormulaModelType = 'Linear' | 'LinearWithInteractions' | 'LinearWithRatios';
export type FormulaConfidenceLevel = 'Low' | 'Medium' | 'High' | 'DeterministicLike';

export interface FormulaDiscoveryMetrics {
  sampleSize: number;
  r2: number;
  mae: number;
  rmse: number;
  residualP95Abs: number;
  residualMeanAbs: number;
}

export interface FormulaDiscoveryTerm {
  featureName: string;
  coefficient: number;
}

export interface FormulaDiscoveryCandidate {
  targetColumn: string;
  terms: FormulaDiscoveryTerm[];
  intercept: number;
  metrics: FormulaDiscoveryMetrics;
  modelType: FormulaModelType;
  confidence: FormulaConfidenceLevel;
  prettyFormula: string;
  notes: string[];
}

export interface FormulaDiscoveryResult {
  datasetId: string;
  targetColumn: string;
  generatedAt: string;
  candidates: FormulaDiscoveryCandidate[];
  consideredColumns: string[];
  excludedColumns: string[];
  notes: string[];
}
