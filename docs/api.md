# API local — versão 1.1

Base padrão: `http://localhost:8787`. O jogo Unity e o portal Farol da Turma usam JSON UTF-8. O navegador nunca abre o SQLite diretamente.

Esta é uma API de demonstração para rede local. Tokens de professora/professor e presença de jogadores ficam na memória; reiniciar o backend encerra esses logins e presenças, mas mantém quizzes e resultados no SQLite.

Erros seguem este formato:

```json
{"error":{"code":"session_not_found","message":"Não encontramos essa sessão. Confira o código com seu professor."}}
```

## Saúde e catálogo público

`GET /health` retorna o estado do servidor:

```json
{"status":"ok","service":"aprender-juntos","version":2,"database":"sqlite"}
```

`GET /api/catalog` retorna somente quizzes publicados para todas as turmas. Ele não contém `correctOptionId` nem `explanation`.

Após entrar em uma sessão, o jogo deve chamar `GET /api/sessions/:code/catalog` com `Authorization: Bearer TOKEN`. Essa rota retorna os quizzes gerais e os quizzes vinculados à turma daquela sessão, ainda sem gabarito.

## Portal de professores

As rotas abaixo usam o prefixo `/api/teacher`.

| Ação | Método e rota | Precisa de token? |
| --- | --- | --- |
| Criar conta | `POST /auth/register` | Não |
| Entrar | `POST /auth/login` | Não |
| Sair | `POST /auth/logout` | Sim |
| Ver painel | `GET /dashboard` | Sim |
| Listar/criar disciplinas | `GET` / `POST /subjects` | Sim |
| Listar/criar turmas | `GET` / `POST /classes` | Sim |
| Listar/criar quizzes | `GET` / `POST /quizzes` | Sim |
| Abrir sessão de turma | `POST /sessions` | Sim |
| Ver resultados | `GET /results` | Sim |

Exemplo de login:

```json
POST /api/teacher/auth/login
{"email":"beatriz@ilhasaber.local","password":"IlhaDoSaber!2026"}
```

O retorno contém um token que o portal envia em `Authorization: Bearer TOKEN`. A conta acima é apenas uma semente de demonstração local.

Para criar um quiz, envie título, disciplina, turma opcional e questões. Cada questão precisa ter de 2 a 5 alternativas e exatamente uma marcada como correta. O backend valida tudo e escolhe os IDs; o portal não pode fornecer pontuação.

## Crianças, sessões e respostas

1. O portal abre uma sessão de uma turma com `POST /api/teacher/sessions`.
2. O jogo entra com `POST /api/sessions/:code/join`, enviando nome e `avatarId`.
3. O servidor devolve `token`, `playerId` e o código normalizado.
4. O jogo consulta o catálogo da sessão, atualiza presença e inicia uma tentativa.

Operações do jogo:

| Ação | Método e rota | Token de criança? |
| --- | --- | --- |
| Criar sessão geral | `POST /api/sessions` | Não |
| Entrar | `POST /api/sessions/:code/join` | Não |
| Ver colegas | `GET /api/sessions/:code/state` | Sim |
| Ver catálogo da turma | `GET /api/sessions/:code/catalog` | Sim |
| Atualizar presença | `PUT /api/sessions/:code/presence` | Sim |
| Sair | `DELETE /api/sessions/:code/players/me` | Sim |
| Iniciar quiz | `POST /api/sessions/:code/attempts` | Sim |
| Enviar resposta | `POST /api/sessions/:code/attempts/:id/answers` | Sim |

Uma resposta é corrigida no servidor. A resposta correta e a explicação chegam apenas depois do envio daquela alternativa. Cada acerto vale 100 pontos; uma resposta errada vale 0 e a criança continua para a próxima questão.

Uma resposta repetida com a mesma alternativa é idempotente: o servidor devolve o mesmo resultado, sem pontos extras. Isso permite ao jogo repetir uma chamada que falhou na rede.

## Limites da demonstração

- Até 40 participantes por sessão e 200 sessões em memória.
- Presença desaparece após 15 segundos sem atividade.
- Token de criança expira após 30 minutos sem atividade.
- Token de professora/professor dura até 8 horas ou até o reinício do backend.
- Não há chat, e-mail de criança, ranking público ou acesso direto ao banco.

Para uso fora da rede local, seria necessário adicionar HTTPS, contas com recuperação de senha, configuração de CORS, backup centralizado e revisão de privacidade.
