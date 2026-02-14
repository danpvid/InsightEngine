# InsightEngine

Projeto estruturado com Clean Architecture, CQRS, MediatR e Domain Notifications.

## 🏗️ Arquitetura

O projeto está organizado em camadas bem definidas:

### 1. **InsightEngine.API**
- Camada de apresentação
- Controllers e endpoints REST
- Configuração do Swagger
- Entry point da aplicação

### 2. **InsightEngine.Application**
- Input e Output Models
- Commands e Queries (CQRS)
- Handlers do MediatR
- AutoMapper configurations
- Validações com FluentValidation

### 3. **InsightEngine.Domain**
- Entidades de domínio
- Interfaces de repositórios
- Lógica de negócio
- Domain Notifications
- Core do sistema

### 4. **InsightEngine.Infra.Data**
- Entity Framework Core
- DbContext
- Repositories
- Unit of Work
- Migrations

### 5. **InsightEngine.Infra.ExternalService**
- Integrações com APIs externas
- HttpClient configurations
- Políticas de retry (Polly)

### 6. **InsightEngine.CrossCutting**
- Injeção de Dependência
- Configurações transversais
- Bootstrap da aplicação

## 🚀 Tecnologias

- .NET 8.0
- Entity Framework Core 8.0
- MediatR 12.2.0
- AutoMapper 13.0.1
- FluentValidation 11.9.0
- Swagger/OpenAPI
- JWT Bearer Authentication
- SQL Server

## 📦 Padrões Implementados

- ✅ CQRS (Command Query Responsibility Segregation)
- ✅ MediatR para mediação de comandos e queries
- ✅ Domain Notifications para gerenciamento de erros
- ✅ Unit of Work para transações
- ✅ Repository Pattern
- ✅ Dependency Injection
- ✅ Clean Architecture
- ✅ JWT Bearer Authentication

## 🔧 Como Executar

1. **Restaurar pacotes:**
```bash
dotnet restore
```

2. **Atualizar connection string** no `appsettings.json` da API

3. **Criar as migrations:**
```bash
cd src/InsightEngine.Infra.Data
dotnet ef migrations add InitialCreate -s ../InsightEngine.API/InsightEngine.API.csproj
```

4. **Aplicar migrations:**
```bash
dotnet ef database update -s ../InsightEngine.API/InsightEngine.API.csproj
```

5. **Executar a aplicação:**
```bash
cd src/InsightEngine.API
dotnet run
```

6. **Acessar o Swagger:**
```
https://localhost:5001/swagger
```

## 🔐 Autenticação

O projeto está configurado com JWT Bearer Token. Para testar:

1. **Obter um token:**
```bash
POST https://localhost:5001/api/auth/login
Content-Type: application/json

{
  "email": "usuario@exemplo.com",
  "password": "senha123"
}
```

2. **Usar o token no Swagger:**
   - Clique no botão "Authorize" 🔒
   - Digite: `Bearer {seu_token_aqui}`
   - Clique em "Authorize"

3. **Configurações JWT** (appsettings.json):
```json
{
  "JwtSettings": {
    "SecretKey": "sua-chave-secreta-minimo-32-caracteres",
    "Issuer": "InsightEngine.API",
    "Audience": "InsightEngine.Client",
    "ExpirationInMinutes": 480
  }
}
```

**⚠️ IMPORTANTE:** Altere a `SecretKey` em produção para uma chave forte e segura!

## 📦 Gerenciamento de Pacotes

Este projeto utiliza **Central Package Management (CPM)** do NuGet para gerenciar versões de pacotes de forma centralizada.

- ✅ Todas as versões são definidas em `Directory.Packages.props`
- ✅ Arquivos `.csproj` apenas referenciam os pacotes (sem versão)
- ✅ Previne conflitos de versão entre projetos
- ✅ Facilita atualizações e manutenção

**Para adicionar um novo pacote:**

1. Adicione a versão no `Directory.Packages.props`:
```xml
<PackageVersion Include="Serilog" Version="3.1.1" />
```

2. Referencie no projeto (.csproj):
```xml
<PackageReference Include="Serilog" />
```

📚 Veja mais detalhes em [PACKAGES.md](PACKAGES.md)

## 📁 Estrutura de Pastas

```
InsightEngine/
├── src/
│   ├── InsightEngine.API/
│   │   ├── Controllers/
│   │   ├── Properties/
│   │   └── Program.cs
│   ├── InsightEngine.Application/
│   │   ├── AutoMapper/
│   │   ├── Commands/
│   │   ├── Models/
│   │   └── Queries/
│   ├── InsightEngine.Domain/
│   │   ├── Core/
│   │   │   ├── Models/
│   │   │   └── Notifications/
│   │   └── Interfaces/
│   ├── InsightEngine.Infra.Data/
│   │   ├── Context/
│   │   ├── Repositories/
│   │   └── UoW/
│   ├── InsightEngine.Infra.ExternalService/
│   └── InsightEngine.CrossCutting/
│       └── IoC/
├── tests/
├── Directory.Packages.props     # 📦 Gerenciamento centralizado de pacotes
├── InsightEngine.sln
├── README.md
├── PACKAGES.md                  # 📚 Documentação de pacotes
├── SECURITY.md
└── .gitignore
```

## 💡 Exemplo de Uso

### Criando um Command

```csharp
public class CreateUserCommand : Command
{
    public string Name { get; set; }
    public string Email { get; set; }
    
    public override bool IsValid()
    {
        // Validação com FluentValidation
        return true;
    }
}
```

### Criando um Handler

```csharp
public class CreateUserCommandHandler : CommandHandler, IRequestHandler<CreateUserCommand, bool>
{
    public CreateUserCommandHandler(
        IDomainNotificationHandler notificationHandler,
        IUnitOfWork unitOfWork) : base(notificationHandler, unitOfWork)
    {
    }
    
    public async Task<bool> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsValid())
        {
            NotifyError("Command", "Dados inválidos");
            return false;
        }
        
        // Lógica de negócio
        
        return await CommitAsync();
    }
}
```

### Criando um Controller

```csharp
[Route("api/[controller]")]
public class UsersController : BaseController
{
    public UsersController(
        IDomainNotificationHandler notificationHandler,
        IMediator mediator) : base(notificationHandler, mediator)
    {
    }
    
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command)
    {
        return await SendCommand(command);
    }
}
```

## 📝 Licença

Este projeto está sob a licença MIT.
