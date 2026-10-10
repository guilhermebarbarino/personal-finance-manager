# Recuperação de senha (Render + Neon + Resend)

Esta entrega NÃO está pronta para publicação automática até executar SQL e configurar email.

## Sequência de implantação
1. Faça snapshot do Neon, teste a migração em branch e depois execute **backend/migrations/20261010_password_recovery.sql** em `production` (oito lançamentos e contas não são modificados).
2. Configure as **Environment Variables no Render**, sem comitar segredos:
   - `RESEND_API_KEY`: chave secreta da API Resend.
   - `RESEND_FROM_EMAIL`: remetente de domínio verificado no Resend, por exemplo `Meu Financeiro <contato@seudominio.com>`.
   - `PASSWORD_RESET_FRONTEND_URL`: URL exata do GitHub Pages, `https://guilhermebarbarino.github.io/personal-finance-manager/`.
3. Confirme a CI do PR e faça merge/deploy no Render; GitHub Pages publica a tela de recuperação.
4. Teste solicitando redefinição para uma conta de teste, abrindo o link recebido, redefinindo senha e entrando com a nova. Confira que a senha anterior e JWT anterior não funcionam.
5. Confirme que um link expirado (>20 min) ou reutilizado não funciona; uma conta desconhecida sempre recebe resposta neutra.

## Segurança
- Tokens aleatórios de 256 bits; apenas SHA-256 é armazenado no Neon.
- Token expira em 20 minutos e só pode ser consumido uma vez.
- Uso atômico via UPDATE condicional dentro de transação.
- Mudança de senha incrementa a versão da sessão e revoga JWTs anteriores.
- Links usam fragment (`#reset-token=...`) para evitar enviar o token ao servidor do site no HTTP request.
- Sem credenciais ou tokens reais no código/PR.
- Limitação por IP para requisição e confirmação: 3 tentativas por 15 minutos.
- O email remetente requer domínio validado. Não use conta de terceiro ou endereços arbitrários.
- Login/cadastro e dados existentes ficam inalterados.

### Observações
- Tokens expirados podem ser removidos periodicamente do banco.
- Envio depende da entrega pelo provedor de e-mails e das configurações corretas.
- No lançamento da versão que inclui `SessionVersion`, sessões antigas precisarão fazer login novamente.
