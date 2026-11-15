# Ilha do Saber

Versão **1.1.0 — demonstração local**.

Ilha do Saber é um jogo educativo 3D para crianças de aproximadamente 7 anos. A criança escolhe um personagem, explora portais e responde quizzes. Professoras e professores usam o portal **Farol da Turma** para criar quizzes, organizar turmas e abrir sessões para o jogo.

Esta versão foi feita para uma aula ou demonstração em rede local. Não é uma publicação na internet: não há HTTPS, hospedagem cloud ou Docker nesta etapa.

## O que existe no projeto

| Parte | Para que serve | Onde roda |
| --- | --- | --- |
| Jogo Unity | Crianças entram, exploram e respondem quizzes. | Unity ou executável gerado |
| Backend Node.js | Corrige respostas, reúne jogadores e protege os dados do quiz. | Porta `8787` |
| Banco SQLite | Guarda professoras, turmas, quizzes, sessões e tentativas concluídas. | Arquivo local do servidor |
| Farol da Turma | Portal web para professoras e professores. | Navegador, porta `8790` |

O navegador e o jogo falam somente com o backend. Nenhum deles acessa o arquivo do banco diretamente.

## Antes de começar

Instale:

- **Unity 6000.0.83f1** pelo Unity Hub;
- **Node.js 22.13 ou mais recente**. Confira com `node --version`.

O backend e o portal usam apenas recursos nativos do Node. Portanto, **não é preciso executar `npm install` ou `npm ci`** para iniciar esta demonstração.

O Docker não faz parte deste repositório por enquanto. Não procure um `docker compose up`: ele ainda não existe aqui.

## Rodar a demonstração completa

Abra dois terminais PowerShell na pasta do projeto.

No primeiro, inicie o backend e o banco:

```powershell
Set-Location C:\Users\pedro\Documents\GitHub\UPX
node backend\server.mjs
```

Deixe esse terminal aberto. A mensagem deve mencionar `http://localhost:8787` e o arquivo SQLite.

No segundo, inicie o portal:

```powershell
Set-Location C:\Users\pedro\Documents\GitHub\UPX
node teacher-portal\server.mjs
```

Abra [http://localhost:8790](http://localhost:8790). A conta de demonstração é:

```text
E-mail: beatriz@ilhasaber.local
Senha: IlhaDoSaber!2026
```

Essa conta existe somente para teste local. Troque ou remova a credencial antes de qualquer uso fora da sala de aula.

No portal:

1. Crie uma turma.
2. Crie e publique um quiz. Você pode vinculá-lo a essa turma ou deixá-lo no catálogo geral.
3. Clique em **Abrir sessão** e anote o código mostrado.

Depois abra esta pasta no Unity Hub com a versão **6000.0.83f1**. No Editor, use **Ilha do Saber > Abrir jogo**, pressione Play, escolha nome e personagem e entre na turma com:

```text
Servidor: http://localhost:8787
Código: o código criado no portal
```

O jogo busca o catálogo da sessão depois que a criança entra. Assim, quizzes exclusivos da turma aparecem somente para aquela sessão.

O modo **Jogar sozinho** funciona sem Node, portal ou rede. Ele usa o catálogo de demonstração em `Assets/Aprender/Resources/quiz-catalog.json` e salva o perfil/progresso no dispositivo.

## Usar com outros computadores da mesma rede

O computador que executa o backend é o servidor da aula. Descubra o IPv4 dele com:

```powershell
ipconfig
```

Nos outros computadores, informe no jogo um endereço parecido com:

```text
http://192.168.1.20:8787
```

Não use `localhost` nesses computadores: ali ele aponta para o próprio computador da criança. Se o Windows pedir autorização de firewall para o Node.js, permita a rede privada da escola.

O portal do professor é local por padrão e abre apenas no computador servidor. As crianças não precisam acessá-lo.

## Banco e dados

Na primeira inicialização, o backend cria o arquivo `backend/data/ilha-do-saber.sqlite`. Ele é ignorado pelo Git para que dados de uma turma não sejam enviados ao repositório. Guarde uma cópia desse arquivo antes de trocar de computador ou reinstalar o projeto.

O SQLite preserva quizzes, turmas, sessões criadas pelo portal e tentativas concluídas. Presenças dos jogadores e tokens de login ficam apenas na memória e expiram ao reiniciar o servidor; isso é intencional para a demonstração local.

O catálogo público enviado ao jogo não contém o gabarito. A resposta correta e a explicação só chegam depois de a criança enviar uma alternativa, quando o backend corrige a questão.

## Plataformas

No desktop, teclado, mouse e controles compatíveis funcionam no macOS e no Windows. Veja [os controles](docs/CONTROLES.md).

Para iPhone, use um Mac com Xcode e o módulo **iOS Build Support** da Unity 6000.0.83f1. No Editor, use **Ilha do Saber > iOS > Preparar iPhone** e depois **Exportar projeto Xcode**. O guia completo está em [docs/IOS.md](docs/IOS.md). A exportação para iPhone ainda precisa ser testada em um Mac e aparelho reais.

Para gerar um executável Windows, instale **Windows Build Support** no Unity Hub e use **Ilha do Saber > Gerar executável Windows**.

## Verificações

```powershell
# Testes do backend, inclusive fluxo portal → sessão → jogo → resultado
node --test backend/test/*.test.mjs

# Teste de integração isolado; ele inicia backend e SQLite temporários
node integration-test.mjs

# Regras do cliente Unity com APIs simuladas (requer PowerShell 7)
pwsh -NoProfile -File tools/verify-client.ps1
```

Esses testes não substituem abrir o projeto no Unity e testar uma aula real. Antes de usar com uma turma, confira o fluxo completo com pelo menos dois participantes na mesma rede.

## Organização

| Caminho | Responsabilidade |
| --- | --- |
| `Assets/Aprender` | Jogo Unity, interface, mundo e personagens |
| `backend` | API, SQLite, autenticação local e correção dos quizzes |
| `teacher-portal` | Portal web Farol da Turma |
| `docs/api.md` | Contrato HTTP entre portal, backend e jogo |
| `docs/database.sql` | Referência para uma futura migração a PostgreSQL; não é o banco em uso |
| `docs/CHANGELOG.md` | Histórico de versões do projeto |

## Cenário e créditos

A praça usa o pacote [Toon Environments — World Creator Pack Lite](https://assetstore.unity.com/packages/3d/environments/landscapes/toon-environments-world-creator-pack-lite-264325). Os personagens usam modelos do pacote Kenney Cube Pets, sob a licença indicada em `Assets/kenney_cube-pets_1.0/License.txt`.
