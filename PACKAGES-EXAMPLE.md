# Exemplo de Uso - Central Package Management

## 🎯 Antes vs Depois

### ❌ ANTES (Sem CPM)

**InsightEngine.API.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
</ItemGroup>
```

**InsightEngine.Application.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
</ItemGroup>
```

**InsightEngine.CrossCutting.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" Version="12.2.0" />
  <PackageReference Include="AutoMapper" Version="12.0.1" />
</ItemGroup>
```

**Problemas:**
- ❌ Versões duplicadas em vários arquivos
- ❌ Risco de versões diferentes
- ❌ Difícil de manter
- ❌ Propenso a erros

---

### ✅ DEPOIS (Com CPM)

**Directory.Packages.props** (Arquivo único na raiz)
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="MediatR" Version="12.2.0" />
    <PackageVersion Include="AutoMapper" Version="12.0.1" />
  </ItemGroup>
</Project>
```

**InsightEngine.API.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="AutoMapper" />
</ItemGroup>
```

**InsightEngine.Application.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="AutoMapper" />
</ItemGroup>
```

**InsightEngine.CrossCutting.csproj**
```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="AutoMapper" />
</ItemGroup>
```

**Benefícios:**
- ✅ Versões centralizadas
- ✅ Consistência garantida
- ✅ Fácil manutenção
- ✅ Previne conflitos

---

## 🔄 Exemplo de Atualização

### Cenário: Atualizar MediatR de 12.2.0 para 13.0.0

#### ❌ SEM CPM (Trabalhoso)
Precisaria editar **3 arquivos**:
1. InsightEngine.API.csproj
2. InsightEngine.Application.csproj
3. InsightEngine.CrossCutting.csproj

```xml
<!-- Alterar em TODOS os projetos -->
<PackageReference Include="MediatR" Version="13.0.0" />
```

#### ✅ COM CPM (Simples)
Editar **apenas 1 arquivo**: `Directory.Packages.props`

```xml
<ItemGroup>
  <!-- Apenas trocar esta linha -->
  <PackageVersion Include="MediatR" Version="13.0.0" />
</ItemGroup>
```

**Todos os projetos são atualizados automaticamente!** 🎉

---

## 🆕 Exemplo: Adicionar Novo Pacote

### Adicionar Serilog ao projeto

**1. Adicione a versão no Directory.Packages.props:**
```xml
<ItemGroup>
  <PackageVersion Include="MediatR" Version="12.2.0" />
  <PackageVersion Include="AutoMapper" Version="12.0.1" />
  <!-- NOVO -->
  <PackageVersion Include="Serilog" Version="3.1.1" />
  <PackageVersion Include="Serilog.AspNetCore" Version="8.0.0" />
</ItemGroup>
```

**2. Referencie apenas nos projetos que precisam:**

**InsightEngine.API.csproj** (precisa do Serilog)
```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="Serilog" />
  <PackageReference Include="Serilog.AspNetCore" />
</ItemGroup>
```

**InsightEngine.Application.csproj** (não precisa do Serilog)
```xml
<ItemGroup>
  <PackageReference Include="MediatR" />
  <PackageReference Include="AutoMapper" />
  <!-- Não adiciona Serilog aqui -->
</ItemGroup>
```

---

## 🔍 Verificar Pacotes

### Comando para listar todos os pacotes

```bash
dotnet list package
```

**Saída:**
```
Pacotes de nível superior
   [net8.0]:
   Pacote de nível superior                      Solicitado   Resolvido
   > AutoMapper                                  (CPM)        12.0.1
   > MediatR                                     (CPM)        12.2.0
   > Swashbuckle.AspNetCore                      (CPM)        6.6.2
```

Note o **(CPM)** indicando que a versão vem do Central Package Management!

### Comando para verificar pacotes desatualizados

```bash
dotnet list package --outdated
```

**Saída:**
```
Pacote de nível superior      Solicitado   Resolvido   Mais recente
> AutoMapper                  (CPM)        12.0.1      13.0.1
```

---

## 📊 Estrutura Visual

```
InsightEngine/
│
├── Directory.Packages.props  ⬅️ ÚNICO LOCAL COM VERSÕES
│   └── Define: MediatR = 12.2.0
│
├── src/
│   ├── InsightEngine.API/
│   │   └── InsightEngine.API.csproj
│   │       └── Referencia: MediatR (sem versão)
│   │
│   ├── InsightEngine.Application/
│   │   └── InsightEngine.Application.csproj
│   │       └── Referencia: MediatR (sem versão)
│   │
│   └── InsightEngine.CrossCutting/
│       └── InsightEngine.CrossCutting.csproj
│           └── Referencia: MediatR (sem versão)
│
└── Resultado: TODOS usam MediatR 12.2.0 ✅
```

---

## ⚙️ Como Funciona

1. **MSBuild detecta** o arquivo `Directory.Packages.props` na raiz
2. **Propriedade habilitada**: `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`
3. **Ao restaurar pacotes**, o NuGet:
   - Lê as referências dos `.csproj` (sem versão)
   - Busca as versões no `Directory.Packages.props`
   - Aplica a versão centralizada

---

## 🎯 Casos de Uso Reais

### Caso 1: Projeto Multi-Camadas (Atual)
✅ **Perfeito!** Garante que todas as camadas usem mesmas versões

### Caso 2: Microserviços no Mono-repo
✅ **Ideal!** Mantém consistência entre múltiplos serviços

### Caso 3: Bibliotecas Compartilhadas
✅ **Recomendado!** Evita conflitos de versão

### Caso 4: Projeto Único Pequeno
⚠️ **Opcional** - Benefícios menores, mas ainda útil para manutenção

---

## 🚨 Erros Comuns e Soluções

### Erro: NU1008
```
error NU1008: Package 'MediatR' version cannot be specified in PackageReference 
when using central package management.
```

**Causa:** Versão especificada no `.csproj` enquanto CPM está ativo

**Solução:**
```xml
<!-- ❌ Errado -->
<PackageReference Include="MediatR" Version="12.2.0" />

<!-- ✅ Correto -->
<PackageReference Include="MediatR" />
```

### Pacote não encontrado

**Causa:** Pacote não definido no `Directory.Packages.props`

**Solução:** Adicione no arquivo central:
```xml
<PackageVersion Include="SeuPacote" Version="1.0.0" />
```

---

## 📚 Recursos

- [Microsoft Docs - Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)
- [NuGet Blog - Introducing CPM](https://devblogs.microsoft.com/nuget/introducing-central-package-management/)

---

**Gerenciamento simplificado e consistente! 🎉**
