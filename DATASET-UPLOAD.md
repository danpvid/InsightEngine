# 📤 Upload de DataSets (CSV) - Documentação

## 🎯 Visão Geral

O sistema suporta upload de arquivos CSV grandes usando **streaming** para evitar estouro de memória. Os arquivos são salvos com nomes seguros baseados em GUID para prevenir colisões e ataques de path traversal.

## 🔐 Segurança Implementada

### 1. **Prevenção de Path Traversal**
- ✅ Sanitização automática de nomes de arquivos
- ✅ Remoção de caracteres especiais e caminhos relativos
- ✅ Validação usando `Path.GetFileName()`
- ✅ Nome do arquivo armazenado: `{GUID}.csv`

### 2. **Validação de Arquivos**
- ✅ Apenas arquivos `.csv` são permitidos
- ✅ Validação de Content-Type (text/csv, application/csv)
- ✅ Limite máximo: **500MB** por arquivo
- ✅ Validação no backend e nas configurações do Kestrel

### 3. **Streaming para Arquivos Grandes**
- ✅ **Buffer de 80KB** para leitura/escrita eficiente
- ✅ **Não carrega o arquivo inteiro na memória**
- ✅ Processamento em chunks
- ✅ Suporte para arquivos de vários GB sem problemas

## 📋 Endpoint de Upload

### **POST** `/api/dataset/upload`

**Autenticação:** Requer JWT Bearer Token

**Content-Type:** `multipart/form-data`

**Parâmetros:**
- `file` (required): Arquivo CSV

**Limites:**
- Tamanho máximo: 500MB
- Formato: apenas `.csv`

### Exemplo de Requisição

#### cURL
```bash
curl -X POST "https://localhost:5001/api/dataset/upload" \
  -H "Authorization: Bearer {seu_token}" \
  -F "file=@/path/to/seu-arquivo.csv"
```

#### PowerShell
```powershell
$token = "seu_token_jwt"
$filePath = "C:\caminho\para\arquivo.csv"

$headers = @{
    "Authorization" = "Bearer $token"
}

$form = @{
    file = Get-Item -Path $filePath
}

Invoke-RestMethod -Uri "https://localhost:5001/api/dataset/upload" `
    -Method Post `
    -Headers $headers `
    -Form $form
```

#### C# HttpClient
```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", token);

using var content = new MultipartFormDataContent();
using var fileStream = File.OpenRead("arquivo.csv");
using var streamContent = new StreamContent(fileStream);

streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
content.Add(streamContent, "file", "arquivo.csv");

var response = await client.PostAsync(
    "https://localhost:5001/api/dataset/upload", 
    content);
```

### Resposta de Sucesso (200 OK)

```json
{
  "success": true,
  "message": "Arquivo enviado com sucesso.",
  "data": {
    "datasetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "originalFileName": "vendas-2024.csv",
    "storedPath": "C:\\uploads\\3fa85f64-5717-4562-b3fc-2c963f66afa6.csv",
    "fileSizeInBytes": 104857600,
    "fileSizeMB": 100.0,
    "createdAt": "2026-02-14T15:30:00Z"
  }
}
```

### Resposta de Erro (400 Bad Request)

```json
{
  "success": false,
  "message": "Apenas arquivos CSV são permitidos."
}
```

### Resposta de Erro (413 Payload Too Large)

```json
{
  "success": false,
  "message": "Arquivo muito grande. Tamanho máximo permitido: 500MB"
}
```

## 📊 Outros Endpoints

### **GET** `/api/dataset`
Lista todos os datasets

**Resposta:**
```json
{
  "success": true,
  "data": [
    {
      "datasetId": "guid",
      "originalFileName": "arquivo.csv",
      "storedFileName": "guid.csv",
      "fileSizeInBytes": 12345,
      "fileSizeMB": 0.01,
      "createdAt": "2026-02-14T15:30:00Z"
    }
  ]
}
```

### **GET** `/api/dataset/{id}`
Obtém informações de um dataset específico

**Resposta:**
```json
{
  "success": true,
  "data": {
    "datasetId": "guid",
    "originalFileName": "arquivo.csv",
    "storedFileName": "guid.csv",
    "storedPath": "/path/to/guid.csv",
    "fileSizeInBytes": 12345,
    "fileSizeMB": 0.01,
    "contentType": "text/csv",
    "createdAt": "2026-02-14T15:30:00Z",
    "updatedAt": null
  }
}
```

## ⚙️ Configuração

### appsettings.json

```json
{
  "FileStorage": {
    "BasePath": "uploads"
  }
}
```

### Alterar Limite de Tamanho

Para alterar o limite de 500MB, edite:

**Program.cs:**
```csharp
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024 * 1024; // 1GB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024 * 1024 * 1024; // 1GB
});
```

**DataSetController.cs:**
```csharp
private const long MaxFileSize = 1024L * 1024 * 1024; // 1GB
```

## 🔧 Otimizações para Performance

### 1. **Buffer Size**
- Padrão: 80KB (81920 bytes)
- Otimizado para balance entre memória e velocidade
- Ajustável no `FileStorageService`

### 2. **Async/Await**
- Todo I/O é assíncrono
- Não bloqueia threads durante upload
- Suporta cancelamento (`CancellationToken`)

### 3. **Streaming**
- Arquivo nunca é carregado inteiramente na memória
- Processamento chunk por chunk
- Escalável para arquivos de vários GB

### 4. **Logging**
- Log a cada 10MB processados
- Monitoramento de progresso
- Facilita debugging

## 🗄️ Estrutura no Banco de Dados

### Tabela: DataSets

```sql
CREATE TABLE DataSets (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    OriginalFileName NVARCHAR(255) NOT NULL,
    StoredFileName NVARCHAR(255) NOT NULL UNIQUE,
    StoredPath NVARCHAR(500) NOT NULL,
    FileSizeInBytes BIGINT NOT NULL,
    ContentType NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    UpdatedAt DATETIME2 NULL
);
```

## 📁 Estrutura de Arquivos

```
InsightEngine/
├── uploads/                                    # Pasta de armazenamento
│   ├── {guid-1}.csv                           # Arquivo armazenado
│   ├── {guid-2}.csv
│   └── ...
├── src/
│   ├── InsightEngine.API/
│   │   └── Controllers/
│   │       └── DataSetController.cs           # Endpoints HTTP
│   ├── InsightEngine.Application/
│   │   ├── Commands/DataSet/
│   │   │   ├── UploadDataSetCommand.cs        # Comando
│   │   │   └── UploadDataSetCommandHandler.cs # Handler
│   │   └── Models/DataSet/
│   │       └── DataSetUploadOutputModel.cs    # Output model
│   ├── InsightEngine.Domain/
│   │   ├── Entities/
│   │   │   └── DataSet.cs                     # Entidade
│   │   └── Interfaces/
│   │       ├── IDataSetRepository.cs          # Interface do repositório
│   │       └── IFileStorageService.cs         # Interface do serviço
│   └── InsightEngine.Infra.Data/
│       ├── Repositories/
│       │   └── DataSetRepository.cs           # Implementação
│       ├── Services/
│       │   └── FileStorageService.cs          # Serviço de storage
│       └── Mappings/
│           └── DataSetMapping.cs              # Mapeamento EF Core
```

## 🚀 Testando no Swagger

1. Execute a aplicação: `dotnet run --project src/InsightEngine.API`
2. Acesse: `https://localhost:5001/swagger`
3. Faça login em `/api/auth/login`
4. Clique no botão **Authorize** 🔒
5. Cole o token obtido no formato: `Bearer {token}`
6. Vá para `/api/dataset/upload`
7. Clique em **Try it out**
8. Escolha um arquivo CSV
9. Clique em **Execute**

## ⚠️ Considerações de Produção

### 1. **Armazenamento**
- ✅ Para produção, considere usar cloud storage (Azure Blob, AWS S3)
- ✅ Implemente política de backup
- ✅ Configure retenção de arquivos

### 2. **Escalabilidade**
- ✅ Use CDN para distribuição
- ✅ Considere compressão (gzip)
- ✅ Implemente queue para processamento assíncrono

### 3. **Segurança**
- ✅ Escaneie arquivos para malware
- ✅ Implemente rate limiting
- ✅ Adicione validação de conteúdo CSV
- ✅ Configure CORS adequadamente

### 4. **Monitoramento**
- ✅ Monitor disk space
- ✅ Track upload metrics
- ✅ Alert on failures
- ✅ Log audit trail

## 📝 Exemplo de Uso Completo

```csharp
// 1. Obter token JWT
POST /api/auth/login
{
  "email": "user@example.com",
  "password": "senha123"
}

// 2. Upload do CSV
POST /api/dataset/upload
Headers: Authorization: Bearer {token}
Body: multipart/form-data with file

// 3. Verificar upload
GET /api/dataset/{datasetId}
Headers: Authorization: Bearer {token}

// 4. Listar todos
GET /api/dataset
Headers: Authorization: Bearer {token}
```

---

**Sistema pronto para receber arquivos CSV grandes de forma segura e eficiente! 🎉**
