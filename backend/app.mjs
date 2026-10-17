import http from 'node:http';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import { catalog as seed, publicCatalog, validateCatalog } from './catalog.mjs';

class ApiError extends Error {
  constructor(status, code, message) { super(message); this.status = status; this.code = code; }
}

const fail = (status, code, message) => { throw new ApiError(status, code, message); };
const CODE_PATTERN = /^[A-Z0-9]{4,8}$/;
const CODE_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

function readJson(req, limit) {
  return new Promise((resolve, reject) => {
    let size = 0;
    let exceeded = false;
    const chunks = [];
    req.on('data', chunk => {
      size += chunk.length;
      if (size > limit) {
        if (!exceeded) reject(new ApiError(413, 'body_too_large', 'A solicitação ficou grande demais.'));
        exceeded = true;
        chunks.length = 0;
      } else if (!exceeded) chunks.push(chunk);
    });
    req.on('end', () => {
      if (exceeded) return;
      if (!size) { resolve({}); return; }
      if (!/^application\/json(?:\s*;|$)/i.test(req.headers['content-type'] || '')) {
        reject(new ApiError(415, 'json_required', 'Envie os dados no formato JSON.')); return;
      }
      try {
        const value = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        if (!value || Array.isArray(value) || typeof value !== 'object') throw new Error();
        resolve(value);
      } catch { reject(new ApiError(400, 'invalid_json', 'Não foi possível ler os dados enviados.')); }
    });
    req.on('error', reject);
    req.on('aborted', () => reject(new ApiError(400, 'aborted', 'A solicitação foi interrompida.')));
  });
}

/** Servidor HTTP sem dependências; opções de relógio e limites tornam as regras testáveis. */
export async function createGameServer(options = {}) {
  const source = options.catalog || seed;
  validateCatalog(source);
  const visibleCatalog = publicCatalog(source);
  const now = options.now || Date.now;
  const dataFile = options.dataFile === undefined ? new URL('./data/attempts.json', import.meta.url) : options.dataFile;
  const presenceTtlMs = options.presenceTtlMs ?? 15_000;
  const playerTtlMs = options.playerTtlMs ?? 30 * 60_000;
  const sessionTtlMs = options.sessionTtlMs ?? 4 * 60 * 60_000;
  const retentionMs = options.retentionMs ?? 30 * 24 * 60 * 60_000;
  const maxSessions = options.maxSessions ?? 200;
  const maxPlayers = options.maxPlayers ?? 40;
  const bodyLimit = options.bodyLimit ?? 16_384;
  const authorizedLimit = options.authorizedLimit ?? 600;
  const publicLimit = options.publicLimit ?? 300;
  const allowedOrigins = new Set(options.allowedOrigins || []);
  const log = options.log || console.error;
  const sessions = new Map();
  const tokens = new Map();
  const limits = new Map();
  const locks = new Map();
  const quizzes = new Map();
  const rooms = new Set(['hub', 'lobby']);
  let completedAttempts = [];
  let saveQueue = Promise.resolve();

  for (const subject of source.subjects) {
    rooms.add(subject.id);
    for (const quiz of subject.quizzes) quizzes.set(quiz.id, { ...quiz, subjectId: subject.id });
  }

  if (dataFile) {
    try {
      const saved = JSON.parse(await readFile(dataFile, 'utf8'));
      if (!Array.isArray(saved) || saved.some(record => !record.id || !Number.isFinite(record.completedAt))) throw new Error('Formato de tentativas inválido.');
      completedAttempts = saved.filter(record => record.completedAt > now() - retentionMs);
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }

  function persist(record) {
    const operation = saveQueue.then(async () => {
      const next = completedAttempts.filter(item => item.completedAt > now() - retentionMs);
      next.push(record);
      if (dataFile) {
        const path = dataFile instanceof URL ? (await import('node:url')).fileURLToPath(dataFile) : dataFile;
        await mkdir(dirname(path), { recursive: true });
        const temp = `${path}.tmp`;
        await writeFile(temp, JSON.stringify(next, null, 2), { encoding: 'utf8', mode: 0o600 });
        await rename(temp, path);
      }
      completedAttempts = next;
    });
    saveQueue = operation.catch(() => {});
    return operation;
  }

  async function exclusive(key, action) {
    const previous = locks.get(key) || Promise.resolve();
    const next = previous.catch(() => {}).then(action);
    locks.set(key, next);
    try { return await next; }
    finally { if (locks.get(key) === next) locks.delete(key); }
  }

  function removePlayer(session, player) {
    tokens.delete(player.token);
    session.players.delete(player.id);
    for (const [id, attempt] of session.attempts) if (attempt.playerId === player.id) session.attempts.delete(id);
  }

  function cleanup() {
    const time = now();
    for (const [code, session] of sessions) {
      if (time - session.lastTouched > sessionTtlMs) {
        for (const player of session.players.values()) tokens.delete(player.token);
        sessions.delete(code);
      } else {
        for (const player of session.players.values()) if (time - player.lastSeen > playerTtlMs) removePlayer(session, player);
      }
    }
    for (const [key, bucket] of limits) if (time >= bucket.endsAt) limits.delete(key);
  }

  function throttle(key, maximum) {
    const time = now();
    const bucket = limits.get(key) || { count: 0, endsAt: time + 60_000 };
    bucket.count++;
    limits.set(key, bucket);
    if (bucket.count > maximum) fail(429, 'too_many_requests', 'Vamos com calma! Tente novamente em um minuto.');
  }

  function sessionFor(rawCode) {
    const code = rawCode.toUpperCase();
    if (!CODE_PATTERN.test(code)) fail(400, 'invalid_code', 'O código deve ter de 4 a 8 letras ou números.');
    const session = sessions.get(code);
    if (!session) fail(404, 'session_not_found', 'Não encontramos essa sessão. Confira o código com seu professor.');
    return session;
  }

  function authenticate(req, session) {
    const match = /^Bearer ([A-Za-z0-9_-]{43})$/i.exec(req.headers.authorization || '');
    const member = match && tokens.get(match[1]);
    if (!member || member.code !== session.code) fail(401, 'unauthorized', 'Entre na sessão novamente para continuar.');
    const player = session.players.get(member.id);
    player.lastSeen = now();
    session.lastTouched = now();
    return player;
  }

  function publicPlayer(player) {
    return { id: player.id, name: player.name, avatarId: player.avatarId, roomId: player.roomId, x: player.x, z: player.z, yaw: player.yaw, quizId: player.quizId };
  }

  function trimReplays(session, playerId) {
    const replays = [...session.attempts.values()].filter(attempt => attempt.playerId === playerId && attempt.completed);
    for (const attempt of replays.slice(0, Math.max(0, replays.length - 20))) session.attempts.delete(attempt.id);
  }

  function generateCode() {
    for (let attempt = 0; attempt < 100; attempt++) {
      const code = Array.from(randomBytes(6), value => CODE_ALPHABET[value % CODE_ALPHABET.length]).join('');
      if (!sessions.has(code)) return code;
    }
    fail(503, 'session_capacity', 'Não conseguimos abrir uma sessão agora. Tente novamente.');
  }

  const server = http.createServer(async (req, res) => {
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.setHeader('Cache-Control', 'no-store');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    const reply = (status, value) => { res.writeHead(status); res.end(value === undefined ? undefined : JSON.stringify(value)); };
    try {
      const origin = req.headers.origin;
      if (origin) {
        res.setHeader('Vary', 'Origin');
        if (!allowedOrigins.has(origin)) fail(403, 'origin_not_allowed', 'Esta origem não tem acesso ao servidor.');
        res.setHeader('Access-Control-Allow-Origin', origin);
      }
      if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
        res.setHeader('Access-Control-Max-Age', '600');
        reply(204); return;
      }
      cleanup();
      const url = new URL(req.url, 'http://localhost');
      const path = url.pathname.replace(/\/$/, '') || '/';
      const ip = req.socket.remoteAddress || 'unknown';
      // Requisições autenticadas usam o próprio token para que uma turma em uma
      // única rede não compartilhe o limite de todos os seus jogadores.
      const bearer = /^Bearer ([A-Za-z0-9_-]{43})$/i.exec(req.headers.authorization || '');
      if (bearer && tokens.has(bearer[1])) throttle(`token:${bearer[1]}`, authorizedLimit);
      else throttle(`ip:${ip}`, publicLimit);
      if (req.method === 'GET' && path === '/health') {
        reply(200, { status: 'ok', service: 'aprender-juntos', version: 1 }); return;
      }
      if (req.method === 'GET' && path === '/api/catalog') { reply(200, visibleCatalog); return; }
      if (req.method === 'POST' && path === '/api/sessions') {
        throttle(`create:${ip}`, 20);
        const body = await readJson(req, bodyLimit);
        if (sessions.size >= maxSessions) fail(503, 'session_capacity', 'Todas as sessões estão ocupadas. Tente novamente em breve.');
        let code = body.code;
        if (code === undefined || code === '') code = generateCode();
        else {
          if (typeof code !== 'string') fail(400, 'invalid_code', 'Informe um código válido.');
          code = code.trim().toUpperCase();
          if (!CODE_PATTERN.test(code)) fail(400, 'invalid_code', 'O código deve ter de 4 a 8 letras ou números.');
          if (sessions.has(code)) fail(409, 'code_in_use', 'Esse código já está em uso. Escolha outro.');
        }
        sessions.set(code, { code, lastTouched: now(), players: new Map(), attempts: new Map() });
        reply(201, { code }); return;
      }
      const route = /^\/api\/sessions\/([A-Za-z0-9]+)\/(join|state|presence|attempts|players\/me)(?:\/([A-Za-z0-9-]+)\/answers)?$/.exec(path);
      if (!route) fail(404, 'not_found', 'Esse caminho não existe.');
      const [, rawCode, action, attemptId] = route;
      const session = sessionFor(rawCode);
      if (req.method === 'POST' && action === 'join' && !attemptId) {
        throttle(`join:${ip}`, 120);
        const body = await readJson(req, bodyLimit);
        const name = typeof body.name === 'string' ? body.name.trim().replace(/\s+/g, ' ') : '';
        if (Array.from(name).length < 2 || Array.from(name).length > 20 || !/^[\p{L}\p{M}\p{N} -]+$/u.test(name)) fail(400, 'invalid_name', 'Use um nome com 2 a 20 letras, números, espaços ou hífen.');
        if (typeof body.avatarId !== 'string' || !/^[A-Za-z0-9_-]{1,48}$/.test(body.avatarId)) fail(400, 'invalid_avatar', 'Escolha um personagem para continuar.');
        if (session.players.size >= maxPlayers) fail(409, 'session_full', 'Essa sessão está cheia. Peça ajuda ao professor.');
        const token = randomBytes(32).toString('base64url');
        const id = randomUUID();
        const player = { id, token, name, avatarId: body.avatarId, roomId: 'hub', x: 0, z: 0, yaw: 0, quizId: '', lastSeen: now() };
        session.players.set(id, player);
        tokens.set(token, { code: session.code, id });
        session.lastTouched = now();
        reply(201, { token, playerId: id, code: session.code }); return;
      }
      const player = authenticate(req, session);
      if (req.method === 'GET' && action === 'state' && !attemptId) {
        reply(200, { code: session.code, selfId: player.id, serverTime: now(), players: [...session.players.values()].filter(item => now() - item.lastSeen <= presenceTtlMs).map(publicPlayer) }); return;
      }
      if (req.method === 'PUT' && action === 'presence' && !attemptId) {
        const body = await readJson(req, bodyLimit);
        if (typeof body.roomId !== 'string' || !rooms.has(body.roomId)) fail(400, 'invalid_room', 'Escolha uma sala disponível.');
        for (const axis of ['x', 'z', 'yaw']) if (typeof body[axis] !== 'number' || !Number.isFinite(body[axis]) || Math.abs(body[axis]) > (axis === 'yaw' ? 100_000 : 1000)) fail(400, 'invalid_position', 'A posição do personagem não é válida.');
        const quizId = body.quizId ?? '';
        if (typeof quizId !== 'string' || (quizId && quizzes.get(quizId)?.subjectId !== body.roomId)) fail(400, 'invalid_quiz', 'Esse quiz não pertence a essa sala.');
        Object.assign(player, { roomId: body.roomId, x: body.x, z: body.z, yaw: ((body.yaw % 360) + 360) % 360, quizId });
        reply(200, { ok: true }); return;
      }
      if (req.method === 'DELETE' && action === 'players/me' && !attemptId) {
        removePlayer(session, player);
        reply(200, { ok: true }); return;
      }
      if (req.method === 'POST' && action === 'attempts' && !attemptId) {
        throttle(`attempt:${player.id}`, 30);
        const body = await readJson(req, bodyLimit);
        const quiz = quizzes.get(body.quizId);
        if (!quiz) fail(400, 'invalid_quiz', 'Escolha um quiz disponível.');
        // Começar novamente abandona a tentativa incompleta. A pontuação nunca
        // vem do cliente; tentativas concluídas continuam disponíveis para replay.
        for (const [id, attempt] of session.attempts) if (attempt.playerId === player.id && !attempt.completed) session.attempts.delete(id);
        trimReplays(session, player.id);
        const attempt = { id: randomUUID(), playerId: player.id, quizId: quiz.id, startedAt: now(), answers: [], score: 0, completed: false };
        session.attempts.set(attempt.id, attempt);
        reply(201, { id: attempt.id, quizId: quiz.id, questionIndex: 0, totalQuestions: quiz.questions.length }); return;
      }
      if (req.method === 'POST' && action === 'attempts' && attemptId) {
        const body = await readJson(req, bodyLimit);
        await exclusive(attemptId, async () => {
          const attempt = session.attempts.get(attemptId);
          if (!attempt || attempt.playerId !== player.id) fail(404, 'attempt_not_found', 'Essa tentativa não foi encontrada. Comece o quiz novamente.');
          const quiz = quizzes.get(attempt.quizId);
          const index = quiz.questions.findIndex(question => question.id === body.questionId);
          if (index < 0) fail(400, 'invalid_question', 'Essa pergunta não pertence ao quiz.');
          const question = quiz.questions[index];
          if (!question.options.some(option => option.id === body.optionId)) fail(400, 'invalid_option', 'Escolha uma das respostas da pergunta.');
          if (index < attempt.answers.length) {
            const previous = attempt.answers[index];
            if (previous.optionId !== body.optionId) fail(409, 'answer_already_submitted', 'Você já respondeu essa pergunta. Continue para a próxima!');
            reply(200, previous.response); return;
          }
          if (index !== attempt.answers.length) fail(409, 'question_out_of_order', 'Responda à pergunta atual antes de continuar.');
          const correct = body.optionId === question.correctOptionId;
          const score = attempt.score + (correct ? 100 : 0);
          const response = { correct, correctOptionId: question.correctOptionId, explanation: question.explanation, score, answered: index + 1, total: quiz.questions.length, completed: index + 1 === quiz.questions.length };
          const answer = { questionId: question.id, optionId: body.optionId, correct, answeredAt: now(), response };
          if (response.completed) {
            await persist({
              id: attempt.id, code: session.code, playerId: player.id, name: player.name, avatarId: player.avatarId,
              quizId: quiz.id, startedAt: attempt.startedAt, completedAt: now(), score, total: quiz.questions.length,
              answers: [...attempt.answers, answer].map(({ questionId, optionId, correct, answeredAt }) => ({ questionId, optionId, correct, answeredAt }))
            });
          }
          attempt.answers.push(answer);
          attempt.score = score;
          attempt.completed = response.completed;
          if (attempt.completed) trimReplays(session, player.id);
          reply(200, response);
        });
        return;
      }
      fail(405, 'method_not_allowed', 'Esta ação não está disponível neste caminho.');
    } catch (error) {
      if (res.headersSent || res.destroyed) return;
      if (error instanceof ApiError) {
        if (error.status === 429) res.setHeader('Retry-After', '60');
        reply(error.status, { error: { code: error.code, message: error.message } });
      } else {
        log('Falha interna no servidor:', error?.message || 'erro desconhecido');
        reply(500, { error: { code: 'internal_error', message: 'Não conseguimos salvar ou concluir a ação. Tente novamente.' } });
      }
    }
  });
  server.requestTimeout = 15_000;
  server.headersTimeout = 10_000;
  server.keepAliveTimeout = 5000;
  return server;
}
