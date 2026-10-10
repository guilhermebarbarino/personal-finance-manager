# Rate limiting de autenticação

Os fluxos sensíveis usam duas camadas complementares:

- limite por IP, aplicado antes do endpoint;
- limite por identificador normalizado, aplicado a e-mail, token ou conta conforme o fluxo.

Os identificadores não são mantidos em texto puro na chave da partição. A API usa HMAC-SHA256 com `JWT_KEY`, e respostas bloqueadas retornam `429`, um corpo genérico e o cabeçalho `Retry-After`.

| Fluxo | Limite por IP | Limite por conta/identificador |
| --- | --- | --- |
| Login | 5 por minuto | 8 por e-mail a cada 15 minutos |
| Cadastro | 3 por hora | 3 por e-mail por hora |
| Recuperar senha / reenviar confirmação | 3 a cada 15 minutos | orçamento compartilhado de 3 por e-mail por hora |
| Redefinir senha | 3 a cada 15 minutos | 5 por token a cada 15 minutos |
| Confirmar e-mail | 10 a cada 15 minutos | 5 por token a cada 15 minutos |
| Alterar senha autenticado | 3 a cada 15 minutos | 5 por conta a cada 15 minutos |

O proxy reverso deve fornecer `X-Forwarded-For`. A API aceita somente o salto mais próximo (`ForwardLimit = 1`) antes de calcular a partição por IP. Por isso, a porta do contêiner deve permanecer acessível apenas por meio do proxy da hospedagem.

As respostas de recuperação continuam uniformes para contas existentes e inexistentes. Tentativas para identificadores inexistentes também consomem o respectivo orçamento, evitando um canal de enumeração.
