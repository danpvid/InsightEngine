# ⚠️ IMPORTANTE - Leia Antes de Começar

## 🔴 Erros no VS Code são Normais

Se você está vendo **erros vermelhos** no VS Code nos arquivos TypeScript do projeto Angular:

```
Cannot find module '@angular/core'
Cannot find module 'rxjs'
Cannot find module 'echarts'
```

**✅ Isso é NORMAL!**

### Por quê?

Os arquivos TypeScript precisam das bibliotecas do Node.js instaladas para funcionar. Essas bibliotecas ainda **não foram instaladas**.

---

## 🚀 Solução (1 comando)

Execute o seguinte comando para instalar todas as dependências:

```bash
cd src/InsightEngine.Web
npm install
```

⏱️ **Tempo estimado:** 2-3 minutos

### O que isso faz?

1. ✅ Baixa todas as bibliotecas do Angular
2. ✅ Instala Material Design
3. ✅ Instala ECharts
4. ✅ Configura o TypeScript
5. ✅ Cria a pasta `node_modules/` (cerca de 400MB)

### Após instalar

**Todos os erros vermelhos desaparecerão!** ✨

---

## 📋 Checklist Antes de Testar

- [ ] .NET 8 SDK instalado (`dotnet --version`)
- [ ] Node.js 18+ instalado (`node --version`)
- [ ] Angular CLI instalado (`ng version`)
- [ ] Dependências instaladas (`npm install` executado)
- [ ] Backend rodando (porta 5001)
- [ ] Frontend rodando (porta 4200)

---

## 🎯 Passo a Passo Completo

### 1. Verificar Pré-requisitos

```bash
# Verificar Node.js
node --version
# Deve mostrar v18.0.0 ou superior

# Verificar .NET
dotnet --version
# Deve mostrar 8.0.0 ou superior

# Verificar Angular CLI (se não tiver, instale)
ng version
# Se não encontrado:
npm install -g @angular/cli
```

### 2. Instalar Dependências do Frontend

```bash
cd src/InsightEngine.Web
npm install
```

**Aguarde a mensagem:** `added XXX packages`

### 3. Iniciar Backend (Terminal 1)

```bash
cd src/InsightEngine.API
dotnet run
```

**Aguarde:** `Now listening on: https://localhost:5001`

### 4. Iniciar Frontend (Terminal 2)

```bash
cd src/InsightEngine.Web
npm start
```

**Aguarde:** `compiled successfully`

### 5. Acessar

Abra o navegador em: **http://localhost:4200**

---

## 🐛 Problemas Comuns

### ❌ Erro: "Cannot find module '@angular/core'"

**Causa:** Dependências não instaladas

**Solução:**
```bash
cd src/InsightEngine.Web
npm install
```

---

### ❌ Erro: "ng: command not found"

**Causa:** Angular CLI não instalado

**Solução:**
```bash
npm install -g @angular/cli
```

---

### ❌ Erro: "Port 4200 is already in use"

**Causa:** Porta ocupada por outro processo

**Solução:**
```bash
# Opção 1: Matar processo na porta 4200
netstat -ano | findstr :4200
taskkill /PID <PID> /F

# Opção 2: Usar outra porta
ng serve --port 4300
```

---

### ❌ Erro: "CORS policy"

**Causa:** Backend não está rodando

**Solução:**
1. Abra terminal separado
2. Execute: `cd src/InsightEngine.API && dotnet run`
3. Aguarde: `Now listening on: https://localhost:5001`
4. Recarregue o frontend

---

### ❌ Erro: "This syntax requires an imported helper but module 'tslib' cannot be found"

**Causa:** Dependências incompletas

**Solução:**
```bash
cd src/InsightEngine.Web
rm -rf node_modules package-lock.json
npm install
```

---

## ✅ Como Saber que Está Tudo Certo?

### Backend
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
```

### Frontend
```
** Angular Live Development Server is listening on localhost:4200 **

√ Compiled successfully.
```

### Browser
- ✅ Página carrega sem erros 404
- ✅ Vê o formulário de upload
- ✅ Toolbar azul com "InsightEngine"
- ✅ Console do navegador sem erros

---

## 🎯 Teste Rápido (Após Setup)

1. **Acesse:** http://localhost:4200
2. **Upload:** samples/ecommerce_sales.csv
3. **Aguarde:** Redirecionamento automático
4. **Veja:** Lista de recomendações
5. **Clique:** Em qualquer gráfico
6. **✅ Sucesso:** Gráfico ECharts aparecer!

---

## 📞 Ainda com Problemas?

### Logs Detalhados

**Backend:**
```bash
cd src/InsightEngine.API
dotnet run --verbosity detailed
```

**Frontend:**
```bash
cd src/InsightEngine.Web
ng serve --verbose
```

### Reinstalar Tudo

```bash
# Frontend
cd src/InsightEngine.Web
rm -rf node_modules package-lock.json
npm cache clean --force
npm install

# Backend (se necessário)
cd src/InsightEngine.API
dotnet clean
dotnet restore
dotnet build
```

---

## 🚀 Scripts Automatizados

Para facilitar, use os scripts prontos:

```bash
# Windows
setup.bat           # Instala dependências
start-demo.bat      # Inicia tudo de uma vez
```

---

## 📚 Documentação Adicional

- **START-HERE.md** - Guia rápido de início
- **QUICK-START-DEMO.md** - Roteiro de teste completo
- **src/InsightEngine.Web/README.md** - Documentação técnica
- **docs/DAY7_FRONTEND_SUMMARY.md** - Resumo do Dia 7

---

## ✨ Resumo

1. ✅ Instale as dependências: `npm install`
2. ✅ Inicie o backend: `dotnet run`
3. ✅ Inicie o frontend: `npm start`
4. ✅ Acesse: http://localhost:4200
5. ✅ Teste: Upload de CSV → Recomendações → Gráfico

**Aproveite o InsightEngine!** 🎊

---

**Nota:** Os erros no VS Code são **temporários** e desaparecerão após `npm install`. Isso é 100% normal em projetos Angular novos.
