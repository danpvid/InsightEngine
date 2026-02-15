import { ApiError } from '../models/api-response.model';

export class HttpErrorUtil {
  static extractErrorMessage(error: any): string {
    if (error?.error?.error?.message) {
      return error.error.error.message;
    }
    
    if (error?.error?.message) {
      return error.error.message;
    }

    if (error?.message) {
      return error.message;
    }

    if (error?.status === 0) {
      return 'Não foi possível conectar ao servidor. Verifique se a API está em execução.';
    }

    if (error?.status === 404) {
      return 'Recurso não encontrado.';
    }

    if (error?.status === 500) {
      return 'Erro interno do servidor.';
    }

    return 'Ocorreu um erro inesperado.';
  }

  static extractApiError(error: any): ApiError | null {
    if (error?.error?.error) {
      return error.error.error as ApiError;
    }
    return null;
  }
}
