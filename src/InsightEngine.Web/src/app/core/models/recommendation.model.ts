export interface ChartRecommendation {
  id: string;
  title: string;
  reason?: string;
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
