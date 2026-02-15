export interface ChartResponse {
  datasetId: string;
  recommendationId: string;
  option: any; // ECharts option object
  meta?: ChartMeta;
}

export interface ChartMeta {
  rowCountReturned: number;
  executionMs?: number;
  chartType?: string;
  generatedAt?: string;
  queryHash?: string;
}
