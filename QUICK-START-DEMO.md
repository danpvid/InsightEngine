# 🚀 Quick Start - Demo do InsightEngine

## Executar a Demo Completa em 3 Passos

### 📋 Pré-requisitos
- ✅ .NET 8 SDK instalado
- ✅ Node.js 18+ e npm instalado
- ✅ Angular CLI 17+ (`npm install -g @angular/cli`)

---

## Passo 1: Instalar Dependências do Frontend

```bash
cd src/InsightEngine.Web
npm install
```

⏱️ **Tempo estimado:** 2-3 minutos

---

## Passo 2: Iniciar o Backend

Abra um **novo terminal** e execute:

```bash
cd src/InsightEngine.API
dotnet run
```

✅ **Aguarde a mensagem:**
```
Now listening on: https://localhost:5001
```

---

## Passo 3: Iniciar o Frontend

Abra **outro terminal** e execute:

```bash
cd src/InsightEngine.Web
npm start
```

ou:

```bash
ng serve
```

✅ **Aguarde a mensagem:**
```
** Angular Live Development Server is listening on localhost:4200 **
```

---

## 🎯 Acessar a Aplicação

Abra seu navegador em:

**http://localhost:4200**

---

## 📊 Testando o Fluxo Completo

### 1️⃣ Upload de Dataset

1. Você será direcionado automaticamente para `/datasets/new`
2. Clique em **"Selecionar Arquivo CSV"**
3. Escolha um dos arquivos de exemplo:
   - `samples/ecommerce_sales.csv` ⭐ **Recomendado para primeiro teste**
   - `samples/employee_records.csv`
   - `samples/financial_transactions.csv`

4. Clique em **"Enviar e Gerar Recomendações"**

⏱️ **Tempo de processamento:** 2-5 segundos

---

### 2️⃣ Visualizar Recomendações

Após o upload:

- ✅ Você será **redirecionado automaticamente**
- ✅ Verá uma **lista de recomendações** de gráficos
- ✅ Cada card mostra:
  - Tipo de gráfico (Line, Bar, etc.)
  - Título e descrição
  - Eixos (X, Y)
  - Justificativa (reasoning)

**Exemplo esperado para ecommerce_sales.csv:**
- 📈 Line Chart: "Sales Over Time"
- 📊 Bar Chart: "Top Products by Sales"
- 🔵 Scatter: "Price vs Quantity Analysis"

---

### 3️⃣ Visualizar Gráfico Interativo

1. **Clique em qualquer recomendação**
2. O gráfico será renderizado com **ECharts**
3. **Interaja com o gráfico:**
   - 🖱️ Hover para ver detalhes
   - 🔍 Zoom com scroll do mouse
   - 👆 Clique na legenda para mostrar/ocultar séries

4. **Veja os metadados:**
   - Linhas retornadas
   - Tempo de execução
   - Tipo de gráfico
   - Data de geração

---

## 🎨 O Que Você Deve Ver

### Tela 1: Upload
![Upload Screen](https://via.placeholder.com/800x400/3f51b5/ffffff?text=Upload+CSV)

- Card com ícone de upload
- Botão azul para selecionar arquivo
- Lista de datasets de exemplo

### Tela 2: Recomendações
![Recommendations](https://via.placeholder.com/800x400/667eea/ffffff?text=Recommendations+Grid)

- Banner roxo com ID do dataset
- Grid de cards com recomendações
- Badges coloridos por tipo de gráfico

### Tela 3: Gráfico
![Chart Viewer](https://via.placeholder.com/800x400/43a047/ffffff?text=Interactive+Chart)

- Breadcrumb "Voltar para Recomendações"
- Gráfico ECharts interativo (520px altura)
- Cards com metadados de execução
- Dicas de interação

---

## 🔥 Testes Rápidos

### Teste 1: Upload de Múltiplos Datasets
```bash
# Upload todos os samples disponíveis
1. ecommerce_sales.csv
2. employee_records.csv
3. financial_transactions.csv
```

### Teste 2: Navegação
```
1. Upload → Recomendações → Gráfico
2. Voltar para Recomendações
3. Escolher outro gráfico
4. Voltar para Upload (via toolbar)
```

### Teste 3: Estados de Erro
```
1. Tente fazer upload de arquivo .txt (deve falhar)
2. Tente arquivo > 50MB (deve falhar)
3. Veja a mensagem de erro formatada
```

---

## 🐛 Troubleshooting Rápido

### ❌ Backend não está rodando
**Erro no console do Angular:**
```
HttpErrorResponse: 0 Unknown Error
```

**Solução:**
```bash
# Terminal 1
cd src/InsightEngine.API
dotnet run
```

---

### ❌ CORS Error
**Erro no console do navegador:**
```
Access to XMLHttpRequest at 'https://localhost:5001' from origin 'http://localhost:4200' has been blocked by CORS
```

**Solução:**
O CORS já está configurado. Certifique-se de que o backend está rodando em `https://localhost:5001`.

---

### ❌ ECharts não aparece
**Sintoma:** Área do gráfico fica em branco

**Solução:**
1. Verifique o console do navegador (F12)
2. Confirme que `ngx-echarts` foi instalado:
```bash
npm list ngx-echarts
```
3. Se necessário, reinstale:
```bash
npm install ngx-echarts echarts
```

---

### ❌ Material não tem estilo
**Sintoma:** Botões e cards sem estilo

**Solução:**
Certifique-se de que o link do Google Fonts está no `index.html`:
```html
<link href="https://fonts.googleapis.com/icon?family=Material+Icons" rel="stylesheet">
```

---

## 📸 Screenshots Esperados

### 1. Upload Page
- ✅ Ícone grande de cloud upload
- ✅ Botão "Selecionar Arquivo CSV"
- ✅ Card azul com informações de requisitos
- ✅ Lista de samples na parte inferior

### 2. Recommendations Page
- ✅ Banner gradient roxo com ID do dataset
- ✅ Grid de 3 colunas (em tela grande)
- ✅ Cards com hover effect (elevação)
- ✅ Badges de tipo de gráfico

### 3. Chart Viewer Page
- ✅ Breadcrumb no topo
- ✅ Gráfico ECharts ocupando largura total
- ✅ Grid de metadados abaixo
- ✅ Card verde com dicas

---

## 🎯 Critérios de Sucesso

### ✅ A demo está funcionando se você conseguir:

1. ✅ Fazer upload de um CSV
2. ✅ Ver as recomendações carregarem
3. ✅ Clicar em uma recomendação
4. ✅ Ver o gráfico renderizar
5. ✅ Interagir com o gráfico (hover, legenda)
6. ✅ Voltar para recomendações
7. ✅ Escolher outro gráfico

---

## 🚀 Próximos Testes

Depois de confirmar que o fluxo básico funciona:

### Teste Avançado 1: Performance
```
Upload: logistics_shipments.csv (maior dataset)
Observe: Tempo de processamento nos metadados
```

### Teste Avançado 2: Validações
```
1. Upload sem selecionar arquivo → Deve falhar
2. Upload arquivo > 50MB → Deve falhar
3. Upload .txt → Deve falhar
```

### Teste Avançado 3: Responsividade
```
1. Abra DevTools (F12)
2. Mude para mobile (375x667)
3. Teste o fluxo completo
```

---

## 📞 Suporte

### Logs Úteis

**Backend:**
```bash
# Ver logs do backend
cd src/InsightEngine.API
dotnet run --verbosity detailed
```

**Frontend:**
```bash
# Ver bundle size
ng build --stats-json
```

### Arquivos de Config

- API URL: `src/InsightEngine.Web/src/environments/environment.development.ts`
- CORS: `src/InsightEngine.API/Program.cs` (linha ~93)
- Routes: `src/InsightEngine.Web/src/app/app.routes.ts`

---

## 🎉 Sucesso!

Se você conseguiu visualizar um gráfico ECharts na tela, **parabéns!** 🎊

O InsightEngine está funcionando end-to-end:
- ✅ Upload de CSV
- ✅ Processamento no backend
- ✅ Recomendações de gráficos
- ✅ Renderização interativa

---

**InsightEngine © 2026** - De CSV para gráficos em segundos! 🚀
