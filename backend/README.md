# Backend do Ilha do Saber

Servidor HTTP local usado pelo jogo e pelo portal Farol da Turma. Ele não usa Express, PostgreSQL ou pacotes npm externos: utiliza módulos nativos do **Node.js 22.13 ou superior** e SQLite nativo.

## Iniciar

Na raiz do projeto:

```powershell
node backend\server.mjs
```

Ou, dentro desta pasta:

```powershell
npm start
```

Não use `npm ci`: não há dependências externas para instalar.

O servidor escuta em `http://localhost:8787` e aceita conexões da rede local. O portal local em `http://localhost:8790` já está autorizado a conversar com esta API.

Para conferir se ele iniciou:

```powershell
Invoke-RestMethod http://localhost:8787/health
```

A resposta deve incluir `"database":"sqlite"`.

## Banco local

Por padrão, o banco é criado em:

```text
backend/data/ilha-do-saber.sqlite
```

Use `DATABASE_FILE` somente se precisar guardar o arquivo em outro lugar:

```powershell
$env:DATABASE_FILE = 'D:\Aula\ilha-do-saber.sqlite'
node server.mjs
```

O arquivo contém dados de professoras, turmas, quizzes, sessões e tentativas concluídas. Ele é ignorado pelo Git. Faça uma cópia dele para backup; não o coloque em repositório público.

## Fluxo da aula

1. Inicie este servidor.
2. Em outro terminal, inicie `node teacher-portal\server.mjs`.
3. No portal, crie turma, quiz e sessão.
4. As crianças entram no jogo usando o código da sessão.
5. O servidor corrige cada resposta e guarda o resultado no SQLite.

O jogo nativo não envia cabeçalho CORS. O portal usa `localhost:8790`, autorizado explicitamente. Esta configuração é para demonstração local; não é uma implantação pública com HTTPS.

## Rede local

Em outro computador, use o IPv4 do computador servidor, por exemplo `http://192.168.1.20:8787`. Não use `localhost` fora do servidor. Autorize o Node.js no firewall para redes privadas, se o Windows solicitar.

## Testes

```powershell
node --test test/*.test.mjs
```

Os testes criam bancos temporários e não alteram o banco da aula.

Contrato completo: [../docs/api.md](../docs/api.md).
