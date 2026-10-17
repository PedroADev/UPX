# Servidor do Aprender Juntos

Servidor real para a demonstração em rede local. Usa somente módulos nativos do Node.js 20 ou superior; não há dependências para instalar.

Na raiz do projeto, execute no PowerShell:

```powershell
Set-Location backend
node server.mjs
```

Mantenha esse terminal aberto. O jogo neste computador usa `http://localhost:8787`. Nos outros computadores, use `http://IP-DO-SERVIDOR:8787` no campo de servidor do jogo. Todos devem estar na mesma rede e entrar com o mesmo código de sessão. O professor pode criar a sessão pela interface do jogo; o portal web será desenvolvido depois.

Para descobrir o IPv4 do computador servidor:

```powershell
ipconfig
```

Se o Windows pedir permissão de rede para o Node.js, permita a rede privada usada pela turma. Redes escolares podem bloquear conexões entre computadores; nesse caso, o responsável pela rede precisa liberar a porta TCP 8787. Teste `http://IP-DO-SERVIDOR:8787/health` no navegador de outro computador.

Para verificar o servidor:

```powershell
node --test test/api.test.mjs
```

Os testes abrem portas locais temporárias e verificam presença entre dois jogadores, isolamento entre sessões, expiração, validação, correção de respostas, ordem de perguntas, reenvio idempotente, concorrência e persistência. Nenhum serviço externo é necessário.

## Configuração

| Variável | Padrão | Uso |
| --- | --- | --- |
| `PORT` | `8787` | Porta HTTP. |
| `HOST` | `0.0.0.0` | Interfaces de rede; `127.0.0.1` restringe ao próprio computador. |
| `DATA_FILE` | `backend/data/attempts.json` | Caminho do histórico; prefira um caminho absoluto ao personalizar. |
| `ALLOWED_ORIGINS` | vazio | Origens web autorizadas, separadas por vírgula, por exemplo `http://localhost:5173,https://escola.example`. |

Exemplo opcional:

```powershell
$env:PORT = '8787'
$env:ALLOWED_ORIGINS = 'http://localhost:5173'
node server.mjs
```

Clientes Unity nativos não enviam `Origin` e funcionam sem configurar CORS. Uma futura versão WebGL precisa cadastrar a origem do site. Não é usado `Access-Control-Allow-Origin: *`.

## Persistência e limites

Sessões, nomes de presença, posições e tokens ficam em memória e desaparecem ao reiniciar o servidor. O código da sessão permite entrar; não é uma senha pessoal. O aluno escolhe um nome de exibição, sem e-mail ou senha.

Cada quiz concluído é salvo atomicamente no arquivo de histórico, com as respostas e pontuação calculadas no servidor. Tokens nunca são gravados nesse arquivo ou retornados na lista de colegas. Registros de mais de 30 dias são descartados ao carregar o histórico e na próxima gravação; se o servidor permanecer desligado, o arquivo deve ser removido conforme a política de retenção da escola. Tentativas incompletas não são persistidas. O histórico atual serve de base para a futura integração; esta versão não tem painel de relatórios ou autenticação de professores.

A lista mostra somente colegas com atividade nos últimos 15 segundos. Um token inativo por 30 minutos expira; uma sessão sem atividade por 4 horas é removida. O limite é de 40 participantes por sessão e 200 sessões por processo. Posições são informadas pelos clientes; a correção e a pontuação dos quizzes são autoritativas.

Esta execução HTTP foi preparada para protótipo em rede local privada. A publicação na internet depende de HTTPS, autenticação de professores, controle de turmas e armazenamento gerenciado. O esquema proposto em [database.sql](../docs/database.sql) prevê esses vínculos; não é executado por este servidor.

O catálogo de demonstração fica em `Assets/Aprender/Resources/quiz-catalog.json`: 3 disciplinas, 6 quizzes e 24 questões. O mesmo arquivo permite prática local na Unity. O servidor retira o gabarito de `GET /api/catalog`; o arquivo incluído no cliente continua contendo respostas para a prática offline, portanto uma distribuição com avaliação formal deve buscar os conteúdos online e não empacotar o gabarito.

Contrato completo: [docs/api.md](../docs/api.md).
