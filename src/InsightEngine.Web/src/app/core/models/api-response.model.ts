export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  errors?: ApiErrorItem[];
  traceId?: string;
}

export interface ApiErrorEnvelope {
  success: false;
  errors: ApiErrorItem[];
  traceId: string;
  status: number;
}

export interface ApiErrorItem {
  code: string;
  message: string;
  target?: string;
}

export interface ApiError {
  code: string;
  message: string;
  target?: string;
  status?: number;
  traceId?: string;
  errors?: ApiErrorItem[];
}
