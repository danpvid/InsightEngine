# InsightEngine - Frontend Angular

Frontend web para a plataforma InsightEngine de visualização de dados automática.

## 🚀 Tecnologias

- **Angular 17** (Standalone Components)
- **Angular Material** (UI Components)
- **ECharts** + **ngx-echarts** (Visualizações)
- **TypeScript**
- **SCSS**

## 📁 Estrutura do Projeto

```
src/
├── app/
│   ├── core/                    # Infraestrutura (models, services, interceptors)
│   │   ├── models/              # Interfaces e tipos
│   │   ├── services/            # Serviços de API
│   │   ├── interceptors/        # HTTP interceptors
│   │   └── util/                # Utilitários
│   │
│   ├── features/                # Features organizadas por domínio
│   │   └── datasets/
│   │       ├── pages/           # Páginas com rotas
│   │       ├── components/      # Componentes reutilizáveis
│   │       └── datasets.routes.ts
│   │
│   ├── shared/                  # Componentes compartilhados
│   │   ├── components/          # Loading, Error, PageHeader
│   │   └── material/            # Material imports centralizados
│   │
│   ├── layout/                  # Layout da aplicação
│   │   └── shell/               # Toolbar + Footer + Router Outlet
│   │
│   ├── app.routes.ts            # Rotas principais
│   └── app.config.ts            # Configuração da aplicação
│
├── environments/                # Configurações de ambiente
└── styles.scss                  # Estilos globais

```

## 🛠️ Instalação

### Pré-requisitos

- **Node.js** 18+ e **npm** 9+
- **Angular CLI** 17+

```bash
npm install -g @angular/cli
```

### Instalar dependências

```bash
cd src/InsightEngine.Web
npm install
```

## ▶️ Como Executar

### 1. Iniciar o Backend

Primeiro, certifique-se de que a API está rodando:

```bash
cd src/InsightEngine.API
dotnet run
```

A API estará disponível em: `https://localhost:5001`

### 2. Iniciar o Frontend

```bash
cd src/InsightEngine.Web
npm start
```

Ou:

```bash
ng serve
```

A aplicação estará disponível em: **http://localhost:4200**

## 🎯 Fluxo de Uso (Dia 7 - MVP)

### 1. Upload de Dataset
- Acesse: `http://localhost:4200/datasets/new`
- Selecione um arquivo CSV
- Clique em "Enviar e Gerar Recomendações"

### 2. Visualizar Recomendações
- Após o upload, você será redirecionado automaticamente
- Ou acesse: `http://localhost:4200/datasets/{datasetId}/recommendations`
- Veja as recomendações de gráficos geradas automaticamente

### 3. Visualizar Gráfico
- Clique em qualquer recomendação
- O gráfico interativo será renderizado com ECharts
- Explore as informações e metadados de execução

## 📂 Datasets de Exemplo

Use os datasets de amostra disponíveis em `samples/`:

- `ecommerce_sales.csv` - Vendas de e-commerce
- `employee_records.csv` - Registros de funcionários
- `financial_transactions.csv` - Transações financeiras
- `healthcare_patients.csv` - Dados de pacientes
- `logistics_shipments.csv` - Dados de logística

## 🔧 Configuração

### Alterar a URL da API

Edite o arquivo `src/environments/environment.development.ts`:

```typescript
export const environment = {
  production: false,
  apiBaseUrl: 'https://localhost:5001' // Altere para sua porta
};
```

### Autenticação (Opcional)

Se o backend exigir autenticação JWT:

1. Obtenha um token através do endpoint `/api/v1/auth/token`
2. O token será salvo automaticamente no `localStorage` como `access_token`
3. O interceptor `authInterceptor` adicionará automaticamente o header `Authorization`

## 🎨 Componentes Principais

### Páginas

- **DatasetUploadPageComponent** - Upload de CSV
- **RecommendationsPageComponent** - Lista de recomendações
- **ChartViewerPageComponent** - Visualização de gráficos com ECharts

### Componentes Compartilhados

- **LoadingBarComponent** - Barra de progresso
- **ErrorPanelComponent** - Exibição de erros da API
- **PageHeaderComponent** - Cabeçalho de página com ícone

### Layout

- **ShellComponent** - Layout principal com toolbar e footer

## 🔌 API Endpoints Usados

```
POST   /api/v1/datasets                                    # Upload CSV
GET    /api/v1/datasets/{id}/recommendations              # Listar recomendações
GET    /api/v1/datasets/{id}/charts/{recommendationId}    # Obter gráfico
```

## 📦 Build para Produção

```bash
npm run build
```

Os arquivos serão gerados em `dist/insight-engine-web/`

## 🧪 Testes

```bash
npm test
```

## 📝 Rotas Disponíveis

| Rota | Descrição |
|------|-----------|
| `/` | Redireciona para `/datasets/new` |
| `/datasets/new` | Página de upload |
| `/datasets/:id/recommendations` | Lista de recomendações |
| `/datasets/:id/charts/:recId` | Visualização de gráfico |

## 🐛 Troubleshooting

### Erro de CORS

Certifique-se de que o backend está configurado com CORS habilitado para `http://localhost:4200`:

```csharp
// Program.cs já configurado com:
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", corsBuilder =>
    {
        corsBuilder.WithOrigins("http://localhost:4200")
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials();
    });
});
```

### Backend não conecta

Verifique se a **porta da API** no arquivo `environment.development.ts` está correta:

```typescript
apiBaseUrl: 'https://localhost:5001' // Ajuste conforme necessário
```

### Erro ao instalar dependências

Limpe o cache do npm e reinstale:

```bash
npm cache clean --force
rm -rf node_modules package-lock.json
npm install
```

## 🎯 DoD (Definition of Done) - Dia 7

- ✅ Projeto Angular criado (standalone + routing + scss)
- ✅ Angular Material + ECharts instalados
- ✅ 3 páginas funcionais (Upload, Recommendations, Chart Viewer)
- ✅ Serviços de API implementados
- ✅ Interceptor de autenticação configurado
- ✅ Layout responsivo com Shell Component
- ✅ CORS configurado no backend
- ✅ Roteamento lazy-loading configurado
- ✅ Estados de loading e erro tratados
- ✅ Gráficos ECharts renderizados corretamente

## 🔮 Próximos Passos (Dia 8+)

- [ ] Autenticação completa (Login/Register)
- [ ] Gerenciamento de datasets (listar, deletar)
- [ ] Exportação de gráficos (PNG, SVG)
- [ ] Temas customizáveis
- [ ] Responsividade mobile aprimorada
- [ ] Testes unitários e E2E
- [ ] PWA (Progressive Web App)

---

**InsightEngine © 2026** - Transformando dados em insights visuais automaticamente
