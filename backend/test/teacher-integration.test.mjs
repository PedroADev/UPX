import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { createGameServer } from '../app.mjs';
import { openDatabase, starsFor } from '../database.mjs';
import { catalog } from '../catalog.mjs';

test('professora publica um quiz e uma sessão de turma o entrega ao jogo', async t => {
  const folder = await mkdtemp(join(tmpdir(), 'farol-integration-'));
  const database = openDatabase(join(folder, 'ilha.sqlite'), catalog);
  const server = await createGameServer({ database, dataFile: null, log: () => {} });
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(async () => { await new Promise(resolve => server.close(resolve)); database.close(); await rm(folder, { recursive: true, force: true }); });
  const base = `http://127.0.0.1:${server.address().port}`;
  const request = async (path, { method = 'GET', token, body } = {}) => {
    const response = await fetch(base + path, { method, headers: { ...(body ? { 'content-type': 'application/json' } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}) }, body: body ? JSON.stringify(body) : undefined });
    return { status: response.status, body: await response.json() };
  };
  const login = await request('/api/teacher/auth/login', { method: 'POST', body: { email: 'beatriz@ilhasaber.local', password: 'IlhaDoSaber!2026' } });
  assert.equal(login.status, 200);
  const teacherToken = login.body.token;
  const turma = await request('/api/teacher/classes', { method: 'POST', token: teacherToken, body: { name: '2º ano A', description: 'Manhã' } });
  assert.equal(turma.status, 201);
  const created = await request('/api/teacher/quizzes', { method: 'POST', token: teacherToken, body: {
    title: 'Missão dos pares', description: 'Encontre um número par.', subjectId: 'matematica', classId: turma.body.class.id,
    questions: [{ text: 'Qual número é par?', explanation: '2 pode ser dividido em dois grupos iguais.', options: [{ text: '3', isCorrect: false }, { text: '2', isCorrect: true }] }]
  } });
  assert.equal(created.status, 201);
  assert.equal((await request('/api/catalog')).body.subjects.flatMap(subject => subject.quizzes).some(quiz => quiz.id === created.body.quiz.id), false);
  const session = await request('/api/teacher/sessions', { method: 'POST', token: teacherToken, body: { classId: turma.body.class.id, code: 'PARES7' } });
  assert.equal(session.status, 201);
  const joined = await request('/api/sessions/PARES7/join', { method: 'POST', body: { name: 'Lia', avatarId: 'cat' } });
  assert.equal(joined.status, 201);
  const scoped = await request('/api/sessions/PARES7/catalog', { token: joined.body.token });
  const scopedQuiz = scoped.body.subjects.flatMap(subject => subject.quizzes).find(quiz => quiz.id === created.body.quiz.id);
  assert.ok(scopedQuiz); assert.equal(JSON.stringify(scopedQuiz).includes('correctOptionId'), false);
  const attempt = await request('/api/sessions/PARES7/attempts', { method: 'POST', token: joined.body.token, body: { quizId: scopedQuiz.id } });
  assert.equal(attempt.status, 201);
  const privateQuestion = database.catalog(turma.body.class.id).subjects.flatMap(subject => subject.quizzes).find(quiz => quiz.id === scopedQuiz.id).questions[0];
  const answer = await request(`/api/sessions/PARES7/attempts/${attempt.body.id}/answers`, { method: 'POST', token: joined.body.token, body: { questionId: privateQuestion.id, optionId: privateQuestion.correctOptionId } });
  assert.equal(answer.body.completed, true); assert.equal(answer.body.score, 100);
  const results = await request('/api/teacher/results', { token: teacherToken });
  assert.equal(results.body.results[0].playerName, 'Lia'); assert.equal(results.body.results[0].quizTitle, 'Missão dos pares');
  assert.equal(results.body.results[0].stars, 3);
});

test('estrelas são calculadas pelo servidor e bancos anteriores recebem a coluna sem perder tentativas', async t => {
  assert.equal(starsFor(0, 4), 1);
  assert.equal(starsFor(200, 4), 1);
  assert.equal(starsFor(300, 4), 2);
  assert.equal(starsFor(400, 4), 3);
  assert.equal(starsFor(100, 0), 0);

  const folder = await mkdtemp(join(tmpdir(), 'farol-legacy-db-'));
  const file = join(folder, 'ilha.sqlite');
  let migrated;
  t.after(async () => {
    migrated?.close();
    await rm(folder, { recursive: true, force: true });
  });
  const legacy = new DatabaseSync(file);
  legacy.exec(`
    CREATE TABLE subjects (
      id TEXT PRIMARY KEY, title TEXT NOT NULL, subtitle TEXT NOT NULL DEFAULT '',
      color TEXT NOT NULL DEFAULT '#4F46E5', icon TEXT NOT NULL DEFAULT 'book', created_at INTEGER NOT NULL
    ) STRICT;
    INSERT INTO subjects VALUES ('matematica', 'Matemática', '', '#4F46E5', 'book', 1);
    CREATE TABLE attempts (
      id TEXT PRIMARY KEY, session_code TEXT NOT NULL, player_id TEXT NOT NULL, player_name TEXT NOT NULL,
      avatar_id TEXT NOT NULL, quiz_id TEXT NOT NULL, started_at INTEGER NOT NULL,
      completed_at INTEGER NOT NULL, score INTEGER NOT NULL, total INTEGER NOT NULL
    ) STRICT;
    INSERT INTO attempts VALUES ('legacy', 'TURMA7', 'player', 'Lia', 'cat', 'quiz', 1, 2, 300, 4);
  `);
  legacy.close();

  migrated = openDatabase(file, catalog);
  const columns = migrated.db.prepare('PRAGMA table_info(attempts)').all().map(column => column.name);
  assert.ok(columns.includes('stars'));
  assert.equal(migrated.db.prepare('SELECT stars FROM attempts WHERE id=?').get('legacy').stars, 2);
});
