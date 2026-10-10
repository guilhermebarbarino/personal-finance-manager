# Etapa 1: confirmação de e-mail

Novas contas recebem `RequiresEmailVerification=true`. Até confirmar a posse do e-mail, não recebem JWT, não acessam endpoints autenticados e não podem redefinir senha pelo fluxo de recuperação. A confirmação registra `EmailVerifiedAt`, revoga sessões anteriores e invalida links pendentes. O token aleatório de 256 bits expira em 30 minutos, é armazenado somente como SHA-256 e consumido atomicamente em transação PostgreSQL.

O link usa o fragmento `#verify-email-token=...`, removido da barra de endereço ao abrir a tela. A confirmação exige clique explícito; um simples GET ou a inspeção de um link por um cliente de e-mail não consome o token. Não há login automático após confirmar.

## Configuração antes de ativar o cadastro

Defina no backend:

- `RESEND_API_KEY`: chave do provedor de e-mail.
- `RESEND_FROM_EMAIL`: remetente autorizado pelo provedor.
- `EMAIL_VERIFICATION_FRONTEND_URL`: URL completa do frontend, incluindo `/personal-finance-manager/` em GitHub Pages. Use HTTPS em produção.
- `PASSWORD_RESET_FRONTEND_URL`: mantenha configurada para a recuperação de senha já existente.

Sem configuração válida, novos cadastros retornam 503 antes de criar uma conta. Se o provedor falhar após a criação, a conta permanece pendente e a interface oferece reenvio; o token do envio que falhou é removido. O reenvio responde de maneira uniforme para e-mails desconhecidos, confirmados ou pendentes. Verifique os limites de envio do provedor e a autorização do remetente; uma resposta aceita pelo provedor não garante entrega na caixa de entrada.

## Compatibilidade e implantação

A atualização `backend/migrations/20261010_email_verification.sql` é aditiva e executada automaticamente na inicialização. Não modifica senhas, proprietários, nomes nem lançamentos. Contas existentes e o administrador criado por configuração mantêm `RequiresEmailVerification=false`, sem inventar uma confirmação de posse: `EmailVerifiedAt` continua nulo. Elas podem pedir confirmação pelo endpoint de reenvio, mas a exigência retroativa será uma decisão de implantação separada.

Antes do deploy, faça backup, execute os testes do PR, valide a atualização em cópia do banco e teste a entrega real com um destinatário autorizado. Não ative a mudança em produção antes de configurar o envio. Um rollback do código anterior pode deixar as colunas e a tabela novas; não apague os dados da migração. O código anterior também deixa de impor a confirmação das contas criadas nesta etapa.

Esta etapa conserva a estratégia geral `EnsureCreatedAsync` e os limites de abuso existentes. A substituição por migrações versionadas e os limites por conta pertencem às próximas etapas, para não misturar mudanças de infraestrutura com o fluxo de identidade.

## Validação

O frontend usa `npm ci` e `npm run build`. Os testes existentes continuam com `dotnet test backend/tests/Finance.Tests/Finance.Tests.csproj`.

A suíte nova exige PostgreSQL exclusivo e vazio para testes. Nunca use o banco de produção: ela simula o esquema anterior removendo as colunas novas nesse banco de teste.

Defina `FINANCE_TEST_DATABASE` com a conexão desse banco e execute `dotnet test backend/tests/Finance.Api.Tests/Finance.Api.Tests.csproj`. O CI fornece um PostgreSQL isolado e testa atualização do esquema, preservação de conta existente, cadastro pendente, bloqueio de login e recuperação, reenvio uniforme, token inválido/expirado, confirmação, rejeição de reutilização e recuperação de falha de envio. O envio é simulado; entrega real de e-mail deve ser verificada na configuração do ambiente.

## Próximas etapas

1. Limites por IP e conta, tentativas de recuperação e proteção contra abuso.
2. Migrações versionadas para todo o esquema e testes de atualização.
3. Isolamento de GET, PUT e DELETE entre usuários.
4. Tokens e CSP, auditoria de dependências no CI.
5. ProblemDetails e tratamento global de erros.
6. Importação em lote com transação e idempotência.
7. Paginação compatível com telas e exportação; health checks completos.
8. Testes de frontend de autenticação, perfil e lançamentos.
