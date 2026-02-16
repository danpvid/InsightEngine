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
}

export interface DatasetProfile {
  datasetId: string;
  rowCount: number;
  sampleSize: number;
  columns: DatasetColumnProfile[];
}

export interface DatasetColumnProfile {
  name: string;
  inferredType: string;
  nullRate: number;
  distinctCount: number;
  topValues: string[];
}
