import { createGameServer } from './app.mjs';

const port = Number(process.env.PORT || 8787);
const host = process.env.HOST || '0.0.0.0';
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT deve ser um número entre 1 e 65535.');
const server = await createGameServer({
  ...(process.env.DATA_FILE ? { dataFile: process.env.DATA_FILE } : {}),
  allowedOrigins: (process.env.ALLOWED_ORIGINS || '').split(',').map(value => value.trim()).filter(Boolean)
});
server.listen(port, host, () => {
  console.log(`Aprender Juntos: http://localhost:${port}`);
  console.log(`Rede local: use http://IP-DESTE-COMPUTADOR:${port} nos outros computadores.`);
  console.log('Sessões e tokens ficam na memória. Tentativas concluídas são salvas em backend/data/attempts.json (ou DATA_FILE).');
});
server.on('error', error => { console.error(`Não foi possível iniciar o servidor: ${error.message}`); process.exitCode = 1; });
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  server.close(() => { process.exitCode = 0; });
});
