// Verificação ponta a ponta isolada: portal -> sessão -> catálogo do jogo -> tentativa -> resultado.
// Execute na raiz com: node integration-test.mjs
// O script cria um SQLite temporário e não altera o banco usado pela demonstração.

import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createGameServer } from './backend/app.mjs';
import { openDatabase, starsFor } from './backend/database.mjs';
import { catalog } from './backend/catalog.mjs';

const demoCredentials = { email: 'beatriz@ilhasaber.local', password: 'IlhaDoSaber!2026' };

function say(message) { console.log(`[integração] ${message}`); }

async function main() {
  const folder = await mkdtemp(join(tmpdir(), 'ilha-integration-'));
  const database = openDatabase(join(folder, 'ilha.sqlite'), catalog);
  const server = await createGameServer({ database, dataFile: null, log: () => {} });
  let listening = false;

  try {
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    listening = true;
    const base = `http://127.0.0.1:${server.address().port}`;
    const request = async (path, { method = 'GET', token, body } = {}) => {
      const response = await fetch(base + path, {
        method,
        headers: { ...(body ? { 'content-type': 'application/json' } : {}), ...(token ? { authorization: `Bearer ${token}` } : {}) },
        body: body ? JSON.stringify(body) : undefined
      });
      const payload = await response.json();
      assert.ok(response.ok, `${method} ${path}: ${payload?.error?.message || response.status}`);
      return payload;
    };

    say('Entrando como professora de demonstração.');
    const login = await request('/api/teacher/auth/login', { method: 'POST', body: demoCredentials });
    const token = login.token;
    const suffix = Date.now().toString(36);

    say('Criando turma e quiz pelo contrato usado pelo portal.');
    const turma = await request('/api/teacher/classes', { method: 'POST', token, body: { name: `Turma integração ${suffix}`, description: 'Criada apenas para o teste isolado.' } });
    const subjects = await request('/api/teacher/subjects', { token });
    const quizTitle = `Quiz integração ${suffix}`;
    const created = await request('/api/teacher/quizzes', {
      method: 'POST', token,
      body: {
        title: quizTitle, description: 'Verifica o caminho completo sem expor o gabarito.',
        subjectId: subjects.subjects[0].id, classId: turma.class.id, status: 'published',
        questions: [
          { text: 'Qual cor aparece no céu em um dia claro?', explanation: 'O céu costuma parecer azul por causa da luz espalhada na atmosfera.', options: [{ text: 'Azul', isCorrect: true }, { text: 'Roxo', isCorrect: false }] },
          { text: 'Quantas patas tem uma aranha?', explanation: 'Aranhas são aracnídeos e têm oito patas.', options: [{ text: 'Seis', isCorrect: false }, { text: 'Oito', isCorrect: true }] },
          { text: 'Qual letra começa a palavra bola?', explanation: 'A palavra bola começa com B.', options: [{ text: 'B', isCorrect: true }, { text: 'D', isCorrect: false }] },
          { text: 'Quanto é 2 mais 2?', explanation: 'Juntar dois grupos de dois resulta em quatro.', options: [{ text: 'Quatro', isCorrect: true }, { text: 'Cinco', isCorrect: false }] }
        ]
      }
    });

    say('Abrindo a sessão que será usada pelo jogo.');
    const opened = await request('/api/teacher/sessions', { method: 'POST', token, body: { classId: turma.class.id } });
    const joined = await request(`/api/sessions/${opened.session.code}/join`, { method: 'POST', body: { name: 'Lia Teste', avatarId: 'cat' } });

    say('Buscando o catálogo da sessão como a Unity faz após entrar.');
    const sessionCatalog = await request(`/api/sessions/${opened.session.code}/catalog`, { token: joined.token });
    const publicQuiz = sessionCatalog.subjects.flatMap(subject => subject.quizzes).find(quiz => quiz.id === created.quiz.id);
    assert.ok(publicQuiz, 'O quiz publicado não chegou ao catálogo da turma.');
    assert.equal(JSON.stringify(publicQuiz).includes('correctOptionId'), false, 'O gabarito não pode sair no catálogo público.');
    assert.equal(JSON.stringify(publicQuiz).includes('explanation'), false, 'A explicação só pode aparecer depois da resposta.');

    say('Respondendo pelo mesmo fluxo usado pelo jogo.');
    const attempt = await request(`/api/sessions/${opened.session.code}/attempts`, { method: 'POST', token: joined.token, body: { quizId: publicQuiz.id } });
    let finalAnswer;
    for (const question of publicQuiz.questions) {
      finalAnswer = await request(`/api/sessions/${opened.session.code}/attempts/${attempt.id}/answers`, {
        method: 'POST', token: joined.token, body: { questionId: question.id, optionId: question.options[0].id }
      });
    }
    assert.equal(finalAnswer.completed, true);

    say('Conferindo o resultado visto pela professora.');
    const results = await request('/api/teacher/results', { token });
    const result = results.results.find(item => item.playerName === 'Lia Teste' && item.quizTitle === quizTitle);
    assert.ok(result, 'A tentativa concluída não apareceu para a professora.');
    assert.equal(result.score, finalAnswer.score);
    assert.equal(result.total, finalAnswer.total);
    assert.equal(result.stars, starsFor(finalAnswer.score, finalAnswer.total));
    say(`Fluxo concluído: ${result.score}/${result.total * 100} pontos e ${result.stars} estrela(s).`);
  } finally {
    if (listening) await new Promise(resolve => server.close(resolve));
    database.close();
    await rm(folder, { recursive: true, force: true });
  }
}

main().catch(error => {
  console.error(`[integração] Falhou: ${error.message}`);
  process.exitCode = 1;
});
