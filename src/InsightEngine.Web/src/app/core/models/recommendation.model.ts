export interface ChartRecommendation {
  id: string;
  title: string;
  reason?: string;  // Backend retorna 'reason', não 'reasoning'
  chart?: {
    library?: string;
    type?: string;
  };
  query?: any;
  optionTemplate?: any;
  xColumn?: string;  // Backend retorna 'xColumn', não 'xAxis'
  yColumn?: string;  // Backend retorna 'yColumn', não 'yAxis'
  aggregation?: string;
  timeBin?: string;
}

// Backend retorna array direto, não um objeto com array dentro
export type RecommendationsResponse = ChartRecommendation[];
