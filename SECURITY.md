# Configuração de Segurança JWT

## ⚠️ ATENÇÃO - SEGURANÇA

### Chaves Secretas (SecretKey)

**NUNCA commit chaves de produção no repositório!**

### Para Desenvolvimento
- Use a chave do `appsettings.Development.json`
- Pode ser commitada (apenas para desenvolvimento)

### Para Produção

Utilize uma das seguintes abordagens:

#### 1. Variáveis de Ambiente (Recomendado)
```bash
# Linux/Mac
export JwtSettings__SecretKey="sua-chave-super-secreta-de-producao-minimo-32-caracteres"

# Windows (PowerShell)
$env:JwtSettings__SecretKey="sua-chave-super-secreta-de-producao-minimo-32-caracteres"

# Windows (CMD)
set JwtSettings__SecretKey=sua-chave-super-secreta-de-producao-minimo-32-caracteres
```

#### 2. Azure Key Vault (Recomendado para Cloud)
```csharp
builder.Configuration.AddAzureKeyVault(/* configuração */);
```

#### 3. User Secrets (Desenvolvimento Local)
```bash
dotnet user-secrets set "JwtSettings:SecretKey" "sua-chave-secreta"
```

#### 4. Arquivo appsettings.Production.json (NÃO commitado)
Adicione `appsettings.Production.json` ao `.gitignore`

### Gerando uma Chave Segura

#### Usando PowerShell:
```powershell
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

#### Usando C#:
```csharp
using System.Security.Cryptography;
var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
Console.WriteLine(key);
```

#### Usando OpenSSL:
```bash
openssl rand -base64 32
```

### Requisitos da Chave

- **Mínimo:** 32 caracteres (256 bits)
- **Recomendado:** 64 caracteres (512 bits)
- Use caracteres aleatórios e complexos
- Nunca reutilize chaves entre ambientes

### Rotação de Chaves

Recomenda-se rotacionar as chaves JWT periodicamente:
- **Desenvolvimento:** A cada 6 meses
- **Produção:** A cada 3 meses ou imediatamente se houver suspeita de comprometimento

### Checklist de Segurança

- [ ] Chave com no mínimo 32 caracteres
- [ ] Chave de produção diferente da de desenvolvimento
- [ ] appsettings.Production.json no .gitignore
- [ ] Usar HTTPS em produção (RequireHttpsMetadata = true)
- [ ] Configurar tempo de expiração adequado (não muito longo)
- [ ] Implementar refresh tokens para sessões longas
- [ ] Validar Issuer e Audience
- [ ] Monitorar tokens suspeitos
