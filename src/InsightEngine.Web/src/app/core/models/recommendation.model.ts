export interface ChartRecommendation {
  id: string;
  title: string;
  reason?: string;
  templateType?: string;
  includedColumns?: string[];
  aggregationPlan?: Record<string, string>;
  reasoning?: string[];
  axisPolicy?: {
    defaultMode?: 'SingleAxisBySemanticType' | 'SeparateAxes';
    maxAxes?: number;
    suggestSeparateAxesWhenScaleRatioAbove?: number;
    allowPerSeriesAxisOverride?: boolean;
  };
  score?: number;
  impactScore?: number;
  scoreCriteria?: string[];
  chart?: {
    library?: string;
    type?: string;
  };
  query?: any;
  optionTemplate?: any;
  xColumn?: string;
  yColumn?: string;
  aggregation?: string;
  timeBin?: string;
}

export type RecommendationsResponse = ChartRecommendation[];
