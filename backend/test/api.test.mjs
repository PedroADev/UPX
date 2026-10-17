import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createGameServer } from '../app.mjs';
import { catalog, validateCatalog } from '../catalog.mjs';

async function fixture(t, options = {}) {
  const server = await createGameServer({ dataFile: null, log: () => {}, ...options });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => new Promise(resolve => server.close(resolve)));
  const base = `http://127.0.0.1:${server.address().port}`;
  async function request(path, { method = 'GET', token, body, headers = {}, raw } = {}) {
    const response = await fetch(`${base}${path}`, {
      method,
      headers: { ...(body !== undefined || raw !== undefined ? { 'content-type': 'application/json' } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}), ...headers },
      ...(body !== undefined ? { body: JSON.stringify(body) } : raw !== undefined ? { body: raw } : {})
    });
    return { status: response.status, headers: response.headers, body: response.status === 204 ? null : await response.json() };
  }
  async function create(code = 'TURMA7') {
    const result = await request('/api/sessions', { method: 'POST', body: { code } });
    assert.equal(result.status, 201);
    return result.body.code;
  }
  async function enter(code, name = 'Ana', avatarId = 'cat') {
    const result = await request(`/api/sessions/${code}/join`, { method: 'POST', body: { name, avatarId } });
    assert.equal(result.status, 201);
    return result.body;
  }
  async function start(code, token, quizId = 'mat-somas') {
    const result = await request(`/api/sessions/${code}/attempts`, { method: 'POST', token, body: { quizId } });
    assert.equal(result.status, 201);
    return result.body;
  }
  return { request, create, enter, start };
}

test('catálogo escolar válido, com seis quizzes, sem gabarito na API', async t => {
  const { request } = await fixture(t);
  validateCatalog(catalog);
  assert.equal(catalog.subjects.length, 3);
  assert.equal(catalog.subjects.flatMap(subject => subject.quizzes).length, 6);
  const health = await request('/health');
  assert.equal(health.status, 200);
  const visible = await request('/api/catalog');
  assert.equal(visible.status, 200);
  assert.equal(visible.headers.get('cache-control'), 'no-store');
  assert.equal(JSON.stringify(visible.body).includes('correctOptionId'), false);
  assert.equal(JSON.stringify(visible.body).includes('explanation'), false);
  const quiz = visible.body.subjects[0].quizzes[0];
  assert.equal(quiz.id, 'mat-somas');
  assert.deepEqual(quiz.questions[0].options, catalog.subjects[0].quizzes[0].questions[0].options);
});

test('criação automática com corpo vazio; códigos normalizados e sem colisão', async t => {
  const { request, create } = await fixture(t);
  const created = await request('/api/sessions', { method: 'POST' });
  assert.equal(created.status, 201);
  assert.match(created.body.code, /^[A-Z2-9]{6}$/);
  assert.equal(await create('  turma7  '), 'TURMA7');
  const duplicate = await request('/api/sessions', { method: 'POST', body: { code: 'turma7' } });
  assert.equal(duplicate.status, 409);
  assert.equal(duplicate.body.error.code, 'code_in_use');
  assert.equal((await request('/api/sessions', { method: 'POST', body: { code: '../' } })).status, 400);
});

test('dois jogadores veem a presença da mesma sessão; outras sessões e tokens ficam isolados', async t => {
  const { request, create, enter } = await fixture(t);
  const code = await create();
  const other = await create('OUTRA7');
  const ana = await enter(code, 'Ana');
  const joao = await enter(code, 'João', 'dog');
  const bia = await enter(other, 'Bia');
  const update = await request(`/api/sessions/${code}/presence`, { method: 'PUT', token: joao.token, body: { roomId: 'matematica', x: 2.5, z: -3, yaw: -90, quizId: 'mat-somas' } });
  assert.equal(update.status, 200);
  const state = await request(`/api/sessions/${code}/state`, { token: ana.token });
  assert.equal(state.status, 200);
  assert.equal(state.body.selfId, ana.playerId);
  assert.equal(typeof state.body.serverTime, 'number');
  assert.equal(state.body.players.length, 2);
  const friend = state.body.players.find(player => player.id === joao.playerId);
  assert.equal(friend.yaw, 270);
  assert.equal(friend.roomId, 'matematica');
  assert.equal(JSON.stringify(state.body).includes(ana.token), false);
  assert.equal(JSON.stringify(state.body).includes(joao.token), false);
  assert.equal((await request(`/api/sessions/${other}/state`, { token: ana.token })).status, 401);
  assert.equal((await request(`/api/sessions/${code}/state`)).status, 401);
  const isolated = await request(`/api/sessions/${other}/state`, { token: bia.token });
  assert.equal(isolated.body.players.length, 1);
  assert.equal(isolated.body.players[0].name, 'Bia');
});

test('colegas desconectados desaparecem após 15s, podem voltar e são removidos ao sair', async t => {
  let time = 100_000;
  const { request, create, enter } = await fixture(t, { now: () => time });
  const code = await create();
  const ana = await enter(code, 'Ana');
  const bia = await enter(code, 'Bia');
  time += 15_001;
  let state = await request(`/api/sessions/${code}/state`, { token: ana.token });
  assert.deepEqual(state.body.players.map(player => player.name), ['Ana']);
  await request(`/api/sessions/${code}/state`, { token: bia.token });
  state = await request(`/api/sessions/${code}/state`, { token: ana.token });
  assert.equal(state.body.players.length, 2);
  assert.equal((await request(`/api/sessions/${code}/players/me`, { method: 'DELETE', token: bia.token })).status, 200);
  assert.equal((await request(`/api/sessions/${code}/state`, { token: bia.token })).status, 401);
  state = await request(`/api/sessions/${code}/state`, { token: ana.token });
  assert.equal(state.body.players.length, 1);
});

test('tentativas são privadas, respostas seguem ordem e pontuação é calculada pelo servidor', async t => {
  const { request, create, enter, start } = await fixture(t);
  const code = await create();
  const ana = await enter(code, 'Ana');
  const bia = await enter(code, 'Bia');
  const attempt = await start(code, ana.token);
  assert.equal(attempt.questionIndex, 0);
  assert.equal(attempt.totalQuestions, 4);
  const path = `/api/sessions/${code}/attempts/${attempt.id}/answers`;
  assert.equal((await request(path, { method: 'POST', token: bia.token, body: { questionId: 'mat-somas-1', optionId: 'b' } })).status, 404);
  let result = await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-2', optionId: 'a', score: 99999 } });
  assert.equal(result.status, 409);
  assert.equal(result.body.error.code, 'question_out_of_order');
  result = await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-1', optionId: 'a', score: 99999 } });
  assert.equal(result.status, 200);
  assert.deepEqual(result.body, { correct: false, correctOptionId: 'b', explanation: catalog.subjects[0].quizzes[0].questions[0].explanation, score: 0, answered: 1, total: 4, completed: false });
  const replay = await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-1', optionId: 'a' } });
  assert.deepEqual(replay.body, result.body);
  const changed = await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-1', optionId: 'b' } });
  assert.equal(changed.status, 409);
  assert.equal(changed.body.error.code, 'answer_already_submitted');
  result = await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-2', optionId: 'a' } });
  assert.equal(result.body.score, 100);
  assert.equal(result.body.answered, 2);
  assert.equal((await request(path, { method: 'POST', token: ana.token, body: { questionId: 'mat-somas-3', optionId: 'inexistente' } })).status, 400);
  assert.equal((await request(path, { method: 'POST', token: ana.token, body: { questionId: 'por-palavras-1', optionId: 'b' } })).status, 400);
});

test('conclusão é persistida uma vez, inclusive com reenvio concorrente da última resposta', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'aprender-api-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const dataFile = join(directory, 'attempts.json');
  const { request, create, enter, start } = await fixture(t, { dataFile });
  const code = await create();
  const player = await enter(code);
  const attempt = await start(code, player.token);
  const quiz = catalog.subjects[0].quizzes[0];
  const path = `/api/sessions/${code}/attempts/${attempt.id}/answers`;
  for (const question of quiz.questions.slice(0, -1)) {
    assert.equal((await request(path, { method: 'POST', token: player.token, body: { questionId: question.id, optionId: question.correctOptionId } })).status, 200);
  }
  const last = quiz.questions.at(-1);
  const [one, two] = await Promise.all([1, 2].map(() => request(path, { method: 'POST', token: player.token, body: { questionId: last.id, optionId: last.correctOptionId } })));
  assert.deepEqual(one.body, two.body);
  assert.equal(one.body.completed, true);
  assert.equal(one.body.score, 400);
  const saved = JSON.parse(await readFile(dataFile, 'utf8'));
  assert.equal(saved.length, 1);
  assert.equal(saved[0].answers.length, 4);
  assert.equal(saved[0].score, 400);
  assert.equal(JSON.stringify(saved).includes(player.token), false);
  // Reiniciar o servidor valida/carrega o histórico, sem recriar tokens de sessão.
  const restarted = await createGameServer({ dataFile });
  await new Promise(resolve => restarted.close(resolve));
});

test('nova tentativa abandona a incompleta e começa com pontuação zerada', async t => {
  const { request, create, enter, start } = await fixture(t);
  const code = await create();
  const player = await enter(code);
  const first = await start(code, player.token);
  await request(`/api/sessions/${code}/attempts/${first.id}/answers`, { method: 'POST', token: player.token, body: { questionId: 'mat-somas-1', optionId: 'b' } });
  const next = await start(code, player.token, 'por-palavras');
  assert.notEqual(next.id, first.id);
  assert.equal(next.questionIndex, 0);
  assert.equal((await request(`/api/sessions/${code}/attempts/${first.id}/answers`, { method: 'POST', token: player.token, body: { questionId: 'mat-somas-2', optionId: 'a' } })).status, 404);
  const answer = await request(`/api/sessions/${code}/attempts/${next.id}/answers`, { method: 'POST', token: player.token, body: { questionId: 'por-palavras-1', optionId: 'a' } });
  assert.equal(answer.body.score, 0);
});

test('falha de gravação não aceita a última resposta e permite retry sem duplicar pontos', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'aprender-disk-failure-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const blockedDirectory = join(directory, 'storage');
  const dataFile = join(blockedDirectory, 'attempts.json');
  const { request, create, enter, start } = await fixture(t, { dataFile });
  // O diretório ainda não existe na inicialização. Este arquivo impede mkdir
  // na conclusão para simular uma falha real do disco, sem mocks do servidor.
  await writeFile(blockedDirectory, 'bloqueio de teste');
  const code = await create();
  const player = await enter(code);
  const attempt = await start(code, player.token);
  const path = `/api/sessions/${code}/attempts/${attempt.id}/answers`;
  const quiz = catalog.subjects[0].quizzes[0];
  for (const question of quiz.questions.slice(0, -1)) {
    const response = await request(path, { method: 'POST', token: player.token, body: { questionId: question.id, optionId: question.correctOptionId } });
    assert.equal(response.status, 200);
  }
  const last = quiz.questions.at(-1);
  const body = { questionId: last.id, optionId: last.correctOptionId, score: 999999, correct: true };
  const failed = await request(path, { method: 'POST', token: player.token, body });
  assert.equal(failed.status, 500);
  assert.equal(failed.body.error.code, 'internal_error');
  await rm(blockedDirectory);
  const retried = await request(path, { method: 'POST', token: player.token, body });
  assert.equal(retried.status, 200);
  assert.equal(retried.body.completed, true);
  assert.equal(retried.body.answered, 4);
  assert.equal(retried.body.score, 400);
  const replay = await request(path, { method: 'POST', token: player.token, body });
  assert.deepEqual(replay.body, retried.body);
  assert.equal(JSON.parse(await readFile(dataFile, 'utf8')).length, 1);
});

test('valida nome, personagem, sala, posição e quiz sem aceitar campos de pontuação', async t => {
  const { request, create, enter } = await fixture(t);
  const code = await create();
  for (const name of ['A', '<b>Ana</b>', 'Nome\nInválido'.repeat(3), 'A'.repeat(21)]) {
    assert.equal((await request(`/api/sessions/${code}/join`, { method: 'POST', body: { name, avatarId: 'cat' } })).status, 400);
  }
  assert.equal((await request(`/api/sessions/${code}/join`, { method: 'POST', body: { name: 'Ana', avatarId: '../cat' } })).status, 400);
  const player = await enter(code, 'João 7');
  const path = `/api/sessions/${code}/presence`;
  const valid = { roomId: 'hub', x: 0, z: 1, yaw: 2, quizId: '' };
  for (const body of [{ ...valid, roomId: 'secret' }, { ...valid, x: '1' }, { ...valid, x: 1001 }, { ...valid, quizId: 'mat-somas' }]) {
    assert.equal((await request(path, { method: 'PUT', token: player.token, body })).status, 400);
  }
  assert.equal((await request(path, { method: 'PUT', token: player.token, body: { ...valid, roomId: 'lobby' } })).status, 200);
});

test('origens web são explícitas, sem exigir CORS dos clientes Unity nativos', async t => {
  const { request } = await fixture(t, { allowedOrigins: ['https://escola.example'] });
  assert.equal((await request('/api/catalog')).status, 200);
  const allowed = await request('/api/catalog', { headers: { Origin: 'https://escola.example' } });
  assert.equal(allowed.status, 200);
  assert.equal(allowed.headers.get('access-control-allow-origin'), 'https://escola.example');
  const blocked = await request('/api/catalog', { headers: { Origin: 'https://outra.example' } });
  assert.equal(blocked.status, 403);
  const preflight = await request('/api/catalog', { method: 'OPTIONS', headers: { Origin: 'https://escola.example' } });
  assert.equal(preflight.status, 204);
});

test('limites protegem corpo e requisições; cabeçalho Authorization falso não contorna limite', async t => {
  const { request } = await fixture(t, { bodyLimit: 128, publicLimit: 3 });
  assert.equal((await request('/api/sessions', { method: 'POST', raw: '{' })).status, 400);
  assert.equal((await request('/api/sessions', { method: 'POST', raw: JSON.stringify({ code: 'A'.repeat(200) }) })).status, 413);
  assert.equal((await request('/health', { token: 'fake' })).status, 200);
  const limited = await request('/health', { token: 'fake' });
  assert.equal(limited.status, 429);
  assert.equal(limited.headers.get('retry-after'), '60');
});

test('tokens expiram por inatividade e sessão expira sem atividade', async t => {
  let time = 1000;
  const { request, create, enter } = await fixture(t, { now: () => time, playerTtlMs: 100, sessionTtlMs: 200 });
  const code = await create();
  const player = await enter(code);
  time += 101;
  assert.equal((await request(`/api/sessions/${code}/state`, { token: player.token })).status, 401);
  time += 100;
  assert.equal((await request(`/api/sessions/${code}/state`, { token: player.token })).status, 404);
});

test('histórico corrompido impede sobrescrita silenciosa na inicialização', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'aprender-invalid-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const dataFile = join(directory, 'attempts.json');
  await writeFile(dataFile, '{invalid');
  await assert.rejects(createGameServer({ dataFile }));
  assert.equal(await readFile(dataFile, 'utf8'), '{invalid');
});
