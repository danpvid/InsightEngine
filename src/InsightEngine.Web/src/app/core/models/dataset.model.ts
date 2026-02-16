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

export interface RawDatasetRowsResponse {
  datasetId: string;
  columns: string[];
  rowCountTotal: number;
  rowCountReturned: number;
  page: number;
  pageSize: number;
  totalPages: number;
  truncated: boolean;
  rows: RawDatasetRow[];
}

export interface RawDatasetRow {
  [key: string]: string | null;
}
