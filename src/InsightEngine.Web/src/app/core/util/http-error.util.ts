import { ApiError, ApiErrorEnvelope, ApiErrorItem } from '../models/api-response.model';

export class HttpErrorUtil {
  static extractErrorMessage(error: unknown): string {
    return this.extractApiError(error)?.message || this.fallbackErrorMessage(error);
  }

  static isRequestAbort(error: unknown): boolean {
    const candidate = error as any;
    if (candidate?.status !== 0) {
      return false;
    }

    return candidate?.name === 'CanceledError' ||
      candidate?.error?.name === 'AbortError' ||
      candidate?.error?.type === 'abort';
  }

  static extractApiError(error: unknown): ApiError | null {
    const envelope = this.extractEnvelope(error);
    if (envelope) {
      return this.mapEnvelopeToApiError(envelope);
    }

    const legacyError = (error as any)?.error?.error as ApiError | undefined;
    if (legacyError?.message) {
      return legacyError;
    }

    return null;
  }

  private static extractEnvelope(error: unknown): ApiErrorEnvelope | null {
    const candidate = (error as any)?.error;
    if (!candidate || candidate.success !== false || !Array.isArray(candidate.errors)) {
      return null;
    }

    return candidate as ApiErrorEnvelope;
  }

  private static mapEnvelopeToApiError(envelope: ApiErrorEnvelope): ApiError {
    const firstError = envelope.errors[0] || { code: 'http_error', message: 'Request failed.' } as ApiErrorItem;

    return {
      code: firstError.code,
      message: firstError.message,
      target: firstError.target,
      status: envelope.status,
      traceId: envelope.traceId,
      errors: envelope.errors
    };
  }

  private static fallbackErrorMessage(error: unknown): string {
    const candidate = error as any;
    if (candidate?.status === 0) {
      return 'Não foi possível conectar ao servidor. Verifique se a API está em execução.';
    }

    if (candidate?.error?.message) {
      return candidate.error.message;
    }

    if (candidate?.message) {
      return candidate.message;
    }

    if (candidate?.status === 404) {
      return 'Recurso não encontrado.';
    }

    if (candidate?.status === 500) {
      return 'Erro interno do servidor.';
    }

    return 'Ocorreu um erro inesperado.';
  }
}
