export interface RuntimeConfig {
  uploadMaxBytes: number;
  uploadMaxMb: number;
  scatterMaxPoints: number;
  histogramBinsMin: number;
  histogramBinsMax: number;
  queryResultMaxRows: number;
  cacheTtlSeconds: number;
  defaultTimeoutSeconds: number;
}
