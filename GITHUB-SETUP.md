# 🚀 Como Publicar o Projeto no GitHub

## Opção 1: Usando a Interface Web do GitHub (Recomendado)

### Passo 1: Criar o Repositório
1. Acesse: https://github.com/new
2. **Repository name**: `InsightEngine`
3. **Description**: `Clean Architecture API with CQRS, MediatR, Domain Notifications, JWT Authentication and Central Package Management`
4. **Visibility**: Escolha Public ou Private
5. ⚠️ **NÃO** marque "Initialize this repository with a README"
6. Clique em **"Create repository"**

### Passo 2: Fazer o Push
Após criar o repositório, execute no terminal:

```bash
cd /c/Users/dan_z/source/repos/InsightEngine
git push -u origin main
```

---

## Opção 2: Usando GitHub CLI (gh)

Se você tem o GitHub CLI instalado:

```bash
cd /c/Users/dan_z/source/repos/InsightEngine

# Criar o repositório e fazer push
gh repo create InsightEngine --public --source=. --remote=origin --push

# OU para repositório privado
gh repo create InsightEngine --private --source=. --remote=origin --push
```

---

## Opção 3: Comando Manual Completo

```bash
cd /c/Users/dan_z/source/repos/InsightEngine

# Já executado:
# git init
# git add .
# git commit -m "Initial commit..."
# git branch -M main
# git remote add origin https://github.com/danpvid/InsightEngine.git

# Execute apenas este comando após criar o repo no GitHub:
git push -u origin main
```

---

## 📝 Informações do Repositório

**URL do Repositório**: https://github.com/danpvid/InsightEngine

**Descrição Sugerida**:
```
🏗️ Clean Architecture API with .NET 8

✨ Features:
• CQRS with MediatR
• Domain Notifications Pattern
• Unit of Work & Repository Pattern
• JWT Bearer Authentication
• Central Package Management
• Swagger with JWT Support
• Entity Framework Core
• AutoMapper & FluentValidation

🎯 Organized in layers: API, Application, Domain, Infrastructure (Data & External Services), and Cross-Cutting
```

**Topics Sugeridas** (no GitHub):
```
dotnet, csharp, clean-architecture, cqrs, mediatr, jwt-authentication, 
entity-framework-core, repository-pattern, unit-of-work, domain-driven-design,
swagger, automapper, fluentvalidation, dependency-injection
```

---

## ✅ Status Atual

- ✅ Repositório Git inicializado
- ✅ Todos os arquivos commitados
- ✅ Branch renomeada para `main`
- ✅ Remote configurado: `https://github.com/danpvid/InsightEngine.git`
- ⏳ **Próximo passo**: Criar repositório no GitHub e fazer push

---

## 🔐 Autenticação

Se for solicitado usuário e senha ao fazer push:

### Opção A: Personal Access Token (Recomendado)
1. Vá em: https://github.com/settings/tokens
2. Clique em "Generate new token" (classic)
3. Dê um nome: `InsightEngine Push`
4. Marque o escopo: `repo`
5. Gere o token e copie
6. No prompt do git, use o token como senha

### Opção B: GitHub CLI
```bash
gh auth login
```

### Opção C: SSH
```bash
git remote set-url origin git@github.com:danpvid/InsightEngine.git
git push -u origin main
```

---

## 🎉 Após o Push

Seu repositório estará disponível em:
https://github.com/danpvid/InsightEngine

E a documentação completa será exibida automaticamente pelo README.md!

---

**Pronto para o push! Execute o comando após criar o repositório no GitHub.** 🚀
