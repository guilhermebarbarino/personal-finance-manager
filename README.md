# Finanças Pessoais

Aplicativo pessoal de receitas e despesas: **React + Vite** no GitHub Pages e **ASP.NET Core 8 / PostgreSQL** em hospedagem separada.

## Arquitetura

- `backend/src/Finance.Domain`: entidades e regras de domínio, sem dependências externas.
- `backend/src/Finance.Application`: portas (interfaces), DTOs e casos de uso.
- `backend/src/Finance.Infrastructure`: adapters de persistência (EF Core / PostgreSQL).
- `backend/src/Finance.Api`: controllers minimal API, JWT, bootstrap do administrador, CORS, validação e limites de requisição.
- `frontend`: React, TypeScript, Recharts, interface responsiva.

## Desenvolvimento local

1. Copie `.env.example` para `.env` e defina `POSTGRES_PASSWORD`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `JWT_KEY` (ao menos 32 caracteres) e `CORS_ORIGIN`.
2. Rode `docker compose up --build` (Docker Compose v2). Banco: `localhost:5432`; API: `http://localhost:8080`.
3. No frontend: `cd frontend && npm install && cp .env.example .env.local && npm run dev`. Acesse `http://localhost:5173`.
4. Configure `VITE_API_URL=http://localhost:8080` em `.env.local`.
5. Entre com email/senha do `.env`. A primeira inicialização cria uma conta administrativa única com hash da senha no banco.

**Segurança:** nunca comite `.env`, credenciais ou chave JWT. A senha inicial existe apenas em variáveis de ambiente do servidor. Troque credenciais após incidentes e use HTTPS em produção. Não há endpoint de cadastro de usuários. O token fica apenas em memória no navegador (novo login após recarregar).

## Deploy

**GitHub Pages hospeda somente o frontend**: ative Settings > Pages > Source: GitHub Actions. Configure em Settings > Secrets and variables > Actions > Variables a variável `VITE_API_URL` com a URL HTTPS da API (por exemplo `https://api.example.com`). O workflow `.github/workflows/pages.yml` gera e publica o frontend. Ele configura `base` com `/<nome-repositorio>/` automaticamente.

Hospede a API .NET em um provedor que suporte containers (ex.: Azure App Service, Render, Fly.io ou VPS) e um PostgreSQL persistente. Configure lá `ConnectionStrings__Default`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `JWT_KEY`, `CORS_ORIGIN` com a URL exata do GitHub Pages, por exemplo `https://seuusuario.github.io`. Faça deploy do Dockerfile em `backend/Dockerfile`. O primeiro boot usa `EnsureCreated` para instalar o schema de MVP: **não é um sistema de migrações**; antes de mudanças de schema em produção, introduza EF Core Migrations e backups.

**Observação:** GitHub Pages é público para projetos Pages normais. Ele expõe os arquivos estáticos e a URL da API, nunca o banco e as senhas. A API exige autenticação JWT em todas as operações financeiras. Configure CORS somente para o frontend publicado; CORS não substitui autenticação.

## API

- `POST /api/auth/login`: `{ "email": "...", "password": "..." }` -> `{ "token": "...", "expiresAt": "..." }`
- `GET /api/transactions?month=2026-10`
- `POST /api/transactions`, `PUT /api/transactions/{id}`, `DELETE /api/transactions/{id}`
- `GET /api/dashboard?year=2026`: séries mensais + totais anuais

POST/PUT: `{ "description":"Salário", "amount":5000, "date":"2026-10-05", "type":"income", "category":"Trabalho" }`. Datas em UTC calendar date; valores positivos com até duas casas decimais. Tudo usa BRL.

## Testes / verificação

- `cd backend && dotnet test` (exige .NET SDK 8)
- `cd frontend && npm install && npm run build`

**Status da entrega:** projeto-fonte inicial; a compilação da API deve ser executada em ambiente com SDK .NET 8 antes de deploy público.
