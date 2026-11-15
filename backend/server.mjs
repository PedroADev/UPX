import { createGameServer } from './app.mjs';
import { openDatabase } from './database.mjs';
import { catalog } from './catalog.mjs';
import { fileURLToPath } from 'node:url';

const port = Number(process.env.PORT || 8787);
const host = process.env.HOST || '0.0.0.0';

if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error('PORT deve ser um número entre 1 e 65535.');
}

const databasePath = process.env.DATABASE_FILE ||
  fileURLToPath(new URL('./data/ilha-do-saber.sqlite', import.meta.url));

const origins = [
  'http://localhost:8790',
  'http://127.0.0.1:8790'
];

const database = openDatabase(databasePath, catalog);

const server = await createGameServer({
  database,
  dataFile: null,
  allowedOrigins: origins
});

server.listen(port, host, () => {
  console.log(`Aprender Juntos: http://localhost:${port}`);
  console.log(`Rede local: use http://IP-DESTE-COMPUTADOR:${port} nos outros computadores.`);
  console.log(`Banco SQLite: ${databasePath}`);
  console.log('Abra o portal em http://localhost:8790 para criar quizzes e sessões de turma.');
});

server.on('error', error => {
  console.error(`Não foi possível iniciar o servidor: ${error.message}`);
  process.exitCode = 1;
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.once(signal, () => {
    server.close(() => {
      database.close();
      process.exitCode = 0;
    });
  });
}