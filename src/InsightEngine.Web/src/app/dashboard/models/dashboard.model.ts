import { ChartRecommendation } from '../../core/models/recommendation.model';

export interface DashboardViewModel {
  dataset?: DashboardDatasetSummary | null;
  kpis: DashboardKpiCard[];
  charts: ChartRecommendation[];
  tables: DashboardTables;
  insights: DashboardInsights;
  metadata: DashboardMetadata;
  lastUpdated?: string | null;
  generation: DashboardGenerationTimestamps;
}

export interface DashboardDatasetSummary {
  id: string;
  name: string;
  rowCount: number;
  columnCount: number;
  createdAt: string;
  updatedAt?: string | null;
  targetColumn?: string | null;
}

export interface DashboardKpiCard {
  key: string;
  label: string;
  value: string;
  trend?: string | null;
}

export interface DashboardTables {
  topFeatures: DashboardTopFeatureRow[];
  dataQuality: DashboardDataQualityRow[];
}

export interface DashboardTopFeatureRow {
  column: string;
  score: number;
  correlation?: number | null;
  varianceNorm?: number | null;
  nullRate: number;
  cardinalityRatio: number;
}

export interface DashboardDataQualityRow {
  column: string;
  nullRate: number;
  outlierRate: number;
  distinctCount: number;
}

export interface DashboardInsights {
  llmExecutiveSummary?: string | null;
  warnings: string[];
}

export interface DashboardMetadata {
  indexAvailable: boolean;
  recommendationsAvailable: boolean;
  formulaAvailable: boolean;
  formulaSummary?: DashboardFormulaSummary | null;
}

export interface DashboardFormulaSummary {
  expression: string;
  error: number;
  confidence: string;
}

export interface DashboardGenerationTimestamps {
  indexGeneratedAt?: string | null;
  recommendationsGeneratedAt?: string | null;
  insightsGeneratedAt?: string | null;
}
