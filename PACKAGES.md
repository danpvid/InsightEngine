# Gerenciamento Centralizado de Pacotes NuGet

## 📦 Central Package Management (CPM)

Este projeto utiliza o recurso **Central Package Management** do NuGet para gerenciar versões de pacotes de forma centralizada, evitando conflitos de versão entre projetos.

## 🎯 Benefícios

- ✅ **Versões Consistentes**: Todos os projetos usam a mesma versão de cada pacote
- ✅ **Gerenciamento Simplificado**: Atualizar versão em um único lugar
- ✅ **Previne Conflitos**: Evita problemas de dependências transitivas
- ✅ **Facilita Manutenção**: Menos código duplicado nos .csproj
- ✅ **Padrão da Indústria**: Seguindo boas práticas do mercado

## 📁 Estrutura

### Directory.Packages.props (Raiz do Repositório)

Este arquivo centraliza **todas** as versões dos pacotes NuGet:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="MediatR" Version="12.2.0" />
    <PackageVersion Include="AutoMapper" Version="12.0.1" />
    <!-- ... outros pacotes ... -->
  </ItemGroup>
</Project>
```

### Arquivos .csproj (Projetos)

Os arquivos de projeto **apenas referenciam** os pacotes, **sem versão**:

```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="AutoMapper" />
</ItemGroup>
```

## 🔧 Como Usar

### Adicionar um Novo Pacote

1. **Adicione a versão no `Directory.Packages.props`:**
```xml
<PackageVersion Include="Serilog" Version="3.1.1" />
```

2. **Referencie no projeto específico (.csproj):**
```xml
<PackageReference Include="Serilog" />
```

### Atualizar a Versão de um Pacote

Atualize **apenas** no arquivo `Directory.Packages.props`:

```xml
<!-- Antes -->
<PackageVersion Include="MediatR" Version="12.2.0" />

<!-- Depois -->
<PackageVersion Include="MediatR" Version="13.0.0" />
```

Todos os projetos que usam este pacote serão atualizados automaticamente.

### Usar Versão Diferente em um Projeto Específico (Não Recomendado)

Se absolutamente necessário, você pode sobrescrever a versão em um projeto específico:

```xml
<PackageReference Include="MediatR" VersionOverride="12.0.0" />
```

⚠️ **Evite fazer isso** - derrota o propósito do gerenciamento centralizado!

## 📋 Pacotes Centralizados no Projeto

### MediatR & CQRS
- `MediatR` - 12.2.0

### AutoMapper
- `AutoMapper` - 12.0.1
- `AutoMapper.Extensions.Microsoft.DependencyInjection` - 12.0.1

### Validação
- `FluentValidation` - 11.9.0

### Entity Framework Core
- `Microsoft.EntityFrameworkCore` - 8.0.2
- `Microsoft.EntityFrameworkCore.SqlServer` - 8.0.2
- `Microsoft.EntityFrameworkCore.Design` - 8.0.2
- `Microsoft.EntityFrameworkCore.Tools` - 8.0.2

### ASP.NET Core
- `Microsoft.AspNetCore.Authentication.JwtBearer` - 8.0.2
- `Microsoft.AspNetCore.Mvc.Versioning` - 5.1.0
- `Microsoft.AspNetCore.Mvc.Versioning.ApiExplorer` - 5.1.0

### JWT
- `System.IdentityModel.Tokens.Jwt` - 7.3.1

### Swagger/OpenAPI
- `Swashbuckle.AspNetCore` - 6.6.2

### HTTP & Resiliência
- `Microsoft.Extensions.Http` - 8.0.0
- `Polly` - 8.3.1

### Dependency Injection
- `Microsoft.Extensions.DependencyInjection.Abstractions` - 8.0.1

## 🚀 Comandos Úteis

### Listar Pacotes Desatualizados
```bash
dotnet list package --outdated
```

### Atualizar Todos os Pacotes
```bash
# Liste os pacotes desatualizados
dotnet list package --outdated

# Atualize as versões no Directory.Packages.props
# Depois restaure
dotnet restore
```

### Verificar Dependências
```bash
dotnet list package --include-transitive
```

### Limpar e Restaurar
```bash
dotnet clean
dotnet restore
dotnet build
```

## 🔍 Troubleshooting

### Erro: "Package version cannot be specified"

Se você vir este erro:
```
error NU1008: Package 'MediatR' version cannot be specified in PackageReference when using central package management.
```

**Solução**: Remova a versão do `.csproj`:
```xml
<!-- Errado -->
<PackageReference Include="MediatR" Version="12.2.0" />

<!-- Correto -->
<PackageReference Include="MediatR" />
```

### Conflito de Versões

Se houver conflito de versões, verifique:

1. **Directory.Packages.props** - Versão está definida?
2. **Dependências Transitivas** - Algum pacote está trazendo versão diferente?

```bash
# Ver todas as dependências
dotnet list package --include-transitive
```

### Desabilitar CPM em um Projeto Específico

Se necessário, você pode desabilitar o CPM em um projeto:

```xml
<PropertyGroup>
  <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
</PropertyGroup>
```

⚠️ **Não recomendado** - mantém consistência usando CPM em todos os projetos.

## 📚 Recursos

- [Documentação Oficial do NuGet CPM](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [Blog Post - Central Package Management](https://devblogs.microsoft.com/nuget/introducing-central-package-management/)

## ✅ Checklist de Boas Práticas

- [x] `Directory.Packages.props` na raiz do repositório
- [x] `ManagePackageVersionsCentrally` definido como `true`
- [x] Todas as versões definidas no `Directory.Packages.props`
- [x] Nenhuma versão nos arquivos `.csproj`
- [x] Projeto compila sem erros
- [x] Documentação atualizada

---

**Gerenciamento de pacotes centralizado e consistente! 🎉**
