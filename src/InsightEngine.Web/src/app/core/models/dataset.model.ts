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
