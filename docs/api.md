# API do jogo — versão 1

Base local: `http://localhost:8787`. Todos os corpos e respostas usam JSON UTF-8. O servidor não aceita pontuação ou indicação de acerto enviada pelo cliente. O futuro painel de professores poderá publicar outro catálogo mantendo este contrato.

Erros têm status HTTP adequado e o formato:

```json
{"error":{"code":"session_not_found","message":"Não encontramos essa sessão. Confira o código com seu professor."}}
```

As operações autenticadas exigem `Authorization: Bearer TOKEN`. O token recebido ao entrar é secreto, serve para uma única sessão e não deve ser compartilhado com colegas. Não há cookies, senha de criança ou cadastro por e-mail.

## Fluxo

1. O professor cria uma sessão.
2. Cada criança escolhe nome e personagem e entra usando o mesmo código.
3. O cliente envia sua presença e busca o estado a cada 0,5 segundo.
4. Os portais alteram `roomId`; o cliente mostra somente colegas na mesma sala.
5. Ao começar um quiz, o cliente abre uma tentativa e envia uma alternativa por pergunta.
6. O servidor corrige, explica e soma 100 pontos por acerto. Uma resposta errada vale zero e permite continuar.
7. Ao concluir, o servidor grava a tentativa. O cliente pode calcular estrelas com `score / (100 * total)`.

## Catálogo

`GET /health` → `200`

```json
{"status":"ok","service":"aprender-juntos","version":1}
```

`GET /api/catalog` → `200`

```json
{
  "subjects": [{
    "id": "matematica",
    "title": "Matemática",
    "subtitle": "Uma aventura com números e formas",
    "color": "#8866D6",
    "icon": "+",
    "quizzes": [{
      "id": "mat-somas",
      "title": "Contar é uma aventura",
      "description": "Vamos contar, juntar e descobrir!",
      "questions": [{
        "id": "mat-somas-1",
        "text": "Você tem 2 maçãs e ganha mais 3. Com quantas maçãs fica?",
        "options": [{"id":"a","text":"4 maçãs"},{"id":"b","text":"5 maçãs"},{"id":"c","text":"6 maçãs"}]
      }]
    }]
  }]
}
```

O exemplo foi abreviado: cada quiz tem quatro perguntas. `correctOptionId` e `explanation` não aparecem no catálogo público. IDs são estáveis; alternativas têm IDs locais à pergunta.

| Disciplina / `roomId` | Quizzes |
| --- | --- |
| `matematica` | `mat-somas`, `mat-formas` |
| `portugues` | `por-palavras`, `por-leitura` |
| `ciencias` | `cie-natureza`, `cie-corpo` |

## Sessões e presença

`POST /api/sessions` com `{}` ou corpo vazio → `201 {"code":"ABC234"}`. Opcionalmente envie `{"code":"TURMA7"}`. Códigos têm de 4 a 8 letras ASCII ou números, são normalizados para maiúsculas e não podem repetir uma sessão ativa (`409 code_in_use`). A geração automática usa seis caracteres sem I, O, 0 e 1.

`POST /api/sessions/:code/join`

```json
{"name":"João","avatarId":"cat"}
```

→ `201`

```json
{"token":"TOKEN-ALEATORIO","playerId":"UUID","code":"ABC234"}
```

O nome tem de 2 a 20 caracteres Unicode: letras, números, marcas de acentuação, espaços ou hífen. Espaços são normalizados. `avatarId` aceita de 1 a 48 letras ASCII, números, `_` e `-`, para permitir novos personagens sem mudar o servidor. O cliente apresenta o catálogo visual de personagens. Nomes iguais são permitidos; a identidade é `playerId`.

`GET /api/sessions/:code/state` (autenticado) → `200`

```json
{
  "code":"ABC234",
  "selfId":"UUID-DO-JOGADOR",
  "serverTime":1792080000000,
  "players":[{"id":"UUID","name":"João","avatarId":"cat","roomId":"hub","x":0,"z":0,"yaw":0,"quizId":""}]
}
```

`serverTime` é Unix em milissegundos. A lista inclui o próprio jogador e os colegas online da sessão inteira. O cliente filtra por `roomId` para desenhar os amigos em sua sala. Tokens e respostas de quiz não são expostos. GET state também mantém o próprio jogador ativo.

`PUT /api/sessions/:code/presence` (autenticado)

```json
{"roomId":"matematica","x":3.2,"z":-1.4,"yaw":90,"quizId":"mat-somas"}
```

→ `200 {"ok":true}`. Salas válidas: `hub`, alias `lobby` e os três IDs de disciplina. `x` e `z` são números finitos entre -1000 e 1000. `yaw` é recebido entre -100000 e 100000 e normalizado para `[0,360)`. `quizId` vazio indica que o jogador está explorando; um quiz informado precisa pertencer à sala. Omissão de `quizId` equivale a vazio. Os demais quatro campos são obrigatórios.

`DELETE /api/sessions/:code/players/me` (autenticado) → `200 {"ok":true}`. Remove a presença e invalida o token. Tentativas concluídas continuam no histórico.

## Tentativas

`POST /api/sessions/:code/attempts` (autenticado)

```json
{"quizId":"mat-somas"}
```

→ `201`

```json
{"id":"UUID-DA-TENTATIVA","quizId":"mat-somas","questionIndex":0,"totalQuestions":4}
```

Uma nova chamada inicia nova tentativa e abandona qualquer tentativa incompleta desse jogador. Um timeout nesse POST pode ser resolvido repetindo o POST: o novo ID recebido passa a ser a tentativa atual, com pontuação zerada. Não é necessário estar fisicamente na sala para iniciar; o cliente controla os portais.

`POST /api/sessions/:code/attempts/:id/answers` (autenticado)

```json
{"questionId":"mat-somas-1","optionId":"b"}
```

→ `200`

```json
{
  "correct":true,
  "correctOptionId":"b",
  "explanation":"2 + 3 = 5. Você fica com 5 maçãs!",
  "score":100,
  "answered":1,
  "total":4,
  "completed":false
}
```

`score` é o acumulado, `answered` é o número de respostas aceitas e `total` é a quantidade de perguntas. Não há penalidade por erro, cronômetro ou disputa de ranking. O gabarito só é revelado após aceitar a resposta àquela pergunta. A última resposta tem `completed:true` e só recebe sucesso após a gravação no disco.

Perguntas devem ser respondidas na ordem do catálogo (`409 question_out_of_order`). Reenviar a **mesma** pergunta e alternativa retorna exatamente a resposta original, sem pontos adicionais, inclusive em chamadas concorrentes. Reenviar uma opção diferente para uma pergunta respondida retorna `409 answer_already_submitted`. Essa proteção permite repetir uma requisição após falha de rede sem avançar duas vezes. O servidor guarda as últimas 20 tentativas concluídas por jogador para replay enquanto a sessão existir. Uma tentativa de outro jogador retorna `404 attempt_not_found`.

Não existem endpoints para ler gabaritos, consultar respostas dos colegas ou modificar o catálogo nesta versão. O catálogo local de prática e o histórico JSON não são servidos como arquivos HTTP.

## Limites e tratamento de conexão

| Limite | Valor |
| --- | --- |
| Corpo JSON | 16 KiB |
| Requisições autenticadas | 600/minuto por token |
| Requisições públicas ou com token inválido | 300/minuto por endereço IP |
| Criação de sessões | 20/minuto por IP |
| Entradas em sessão | 120/minuto por IP |
| Inícios de tentativa | 30/minuto por jogador |
| Participantes por sessão | 40 |
| Sessões por processo | 200 |
| Presença online | 15 segundos sem atividade |
| Expiração de token inativo | 30 minutos |
| Expiração de sessão inativa | 4 horas |

`429` inclui `Retry-After: 60`. O limite por token permite que computadores da mesma turma compartilhem o endereço IP. O servidor não confia em `X-Forwarded-For`; uma implantação atrás de proxy deve configurar um limite na borda e revisar esse comportamento.

Erros principais: `400` entrada inválida, `401` token ausente/expirado ou de outra sessão, `403` origem web não autorizada, `404` sessão/tentativa inexistente, `409` conflito de código/ordem/resposta/sessão cheia, `413` corpo grande, `415` tipo diferente de JSON, `429` limite, `500` falha de persistência, `503` capacidade de sessões. As mensagens são adequadas para exibição em português. Não exponha mensagens internas do Node no jogo.

Se uma requisição de resposta falhar, mantenha a pergunta e repita a mesma alternativa. Se a sessão/token expirar, retorne ao login e entre novamente. O cliente não deve trocar uma tentativa online por correção offline no meio dela; a prática local é uma sessão separada.
