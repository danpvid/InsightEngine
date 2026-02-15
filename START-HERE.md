# 🎯 Execução Rápida - 3 Comandos

## Opção 1: Scripts Automatizados (Recomendado)

### Windows

```bash
# 1. Setup inicial (apenas primeira vez)
setup.bat

# 2. Iniciar tudo de uma vez
start-demo.bat
```

Ou separadamente:
```bash
# Terminal 1
start-backend.bat

# Terminal 2
start-frontend.bat
```

---

## Opção 2: Comandos Manuais

### Passo 1: Instalar Dependências (apenas primeira vez)

```bash
cd src/InsightEngine.Web
npm install
```

### Passo 2: Iniciar Backend

**Terminal 1:**
```bash
cd src/InsightEngine.API
dotnet run
```

✅ Aguarde: `Now listening on: https://localhost:5001`

### Passo 3: Iniciar Frontend

**Terminal 2:**
```bash
cd src/InsightEngine.Web
npm start
```

✅ Aguarde: `compiled successfully`

### Acessar

**http://localhost:4200**

---

## 📦 O Que Cada Script Faz

| Script | Descrição |
|--------|-----------|
| `setup.bat` | Verifica pré-requisitos e instala dependências npm |
| `start-backend.bat` | Inicia apenas a API (.NET) |
| `start-frontend.bat` | Inicia apenas o Angular |
| `start-demo.bat` | Inicia backend + frontend automaticamente |

---

## ✅ Checklist Pré-Execução

- [ ] .NET 8 SDK instalado
- [ ] Node.js 18+ instalado
- [ ] Angular CLI instalado (`npm install -g @angular/cli`)
- [ ] Porta 5001 livre (backend)
- [ ] Porta 4200 livre (frontend)

---

## 🔥 Testar Rapidamente

Após iniciar ambos os servidores:

1. Acesse **http://localhost:4200**
2. Upload: `samples/ecommerce_sales.csv`
3. Veja as recomendações
4. Clique em qualquer gráfico
5. ✅ **Sucesso!** Se ver um gráfico ECharts, está funcionando!

---

## 🐛 Problemas Comuns

### Backend não inicia
```bash
# Verifique se a porta está livre
netstat -ano | findstr :5001

# Se estiver em uso, mate o processo ou mude a porta em appsettings.json
```

### Frontend não compila
```bash
# Limpe e reinstale
cd src/InsightEngine.Web
rm -rf node_modules package-lock.json
npm install
```

### Erro de CORS
- ✅ Já está configurado!
- Certifique-se de que o backend está rodando
- Confirme que está acessando `http://localhost:4200` (não outro endereço)

---

**Pronto para testar!** 🚀
