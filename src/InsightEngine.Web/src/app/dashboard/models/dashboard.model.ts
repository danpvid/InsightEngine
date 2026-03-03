import { ChartRecommendation } from '../../core/models/recommendation.model';

export interface DashboardViewModel {
  dataset?: DashboardDatasetSummary | null;
  kpis: DashboardKpiCard[];
  charts?: ChartRecommendation[];
  heroChart?: ChartRecommendation | null;
  secondaryCharts?: ChartRecommendation[];
  tables: DashboardTables;
  insights: DashboardInsights;
  metadata: DashboardMetadata;
  renderingHints?: DashboardRenderingHints | null;
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
  topCategories: DashboardCategorySummaryRow[];
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

export interface DashboardCategorySummaryRow {
  column: string;
  category: string;
  count: number;
}

export interface DashboardInsights {
  llmExecutiveSummary?: string | null;
  executiveBullets: string[];
  keyDrivers: string[];
  warnings: string[];
  nextActions: string[];
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

export interface DashboardRenderingHints {
  numberFormat: {
    mode: string;
    locale: string;
  };
  multiSeriesPolicy: {
    maxSeries: number;
    maxLegendItems: number;
  };
}
