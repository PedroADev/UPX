// integration-test.mjs – end‑to‑end test for UPX (Ilha do Saber)
// Run with: node integration-test.mjs
// Requires Node 20+ (fetch is built‑in).

import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname } from 'node:path';

const LOG_PATH = fileURLToPath(new URL('./integration-test.log', import.meta.url));
function log(message) {
  const time = new Date().toISOString();
  const line = `[${time}] ${message}\n`;
  fs.appendFileSync(LOG_PATH, line);
  console.log(message);
}

async function main() {
  const backend = 'http://127.0.0.1:8787';

  // 1️⃣ Login (demo teacher)
  log('🔐 Logging in as demo teacher');
  const loginResp = await fetch(`${backend}/api/teacher/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'beatriz@ilhasaber.local', password: 'IlhaDoSaber!2026' })
  });
  if (!loginResp.ok) throw new Error('Login failed');
  const { token, teacher } = await loginResp.json();
  const authHeader = { Authorization: `Bearer ${token}` };
  log(`✅ Logged in as ${teacher.name}`);

  // 2️⃣ Create a class (turma)
  log('🏫 Creating class');
  const classResp = await fetch(`${backend}/api/teacher/classes`, {
    method: 'POST',
    headers: { ...authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({ name: 'Turma Teste', description: 'Turma para integração' })
  });
  const { class: createdClass } = await classResp.json();
  log(`✅ Created class id=${createdClass.id}`);

  // 3️⃣ Create a quiz with 4 questions
  log('✏️ Creating quiz');
  // fetch a subject id first
  const subjectsResp = await fetch(`${backend}/api/teacher/subjects`, { headers: authHeader });
  const { subjects } = await subjectsResp.json();
  const subjectId = subjects[0].id;

  const quizPayload = {
    title: 'Quiz Integração',
    description: 'Quiz de teste para integração',
    subjectId,
    classId: createdClass.id,
    status: 'published',
    questions: [
      {
        text: 'Pergunta 1',
        explanation: 'Exp 1',
        options: [
          { text: 'A', isCorrect: true },
          { text: 'B', isCorrect: false }
        ]
      },
      {
        text: 'Pergunta 2',
        explanation: 'Exp 2',
        options: [
          { text: 'C', isCorrect: false },
          { text: 'D', isCorrect: true }
        ]
      },
      {
        text: 'Pergunta 3',
        explanation: 'Exp 3',
        options: [
          { text: 'E', isCorrect: true },
          { text: 'F', isCorrect: false }
        ]
      },
      {
        text: 'Pergunta 4',
        explanation: 'Exp 4',
        options: [
          { text: 'G', isCorrect: false },
          { text: 'H', isCorrect: true }
        ]
      }
    ]
  };
  const quizResp = await fetch(`${backend}/api/teacher/quizzes`, {
    method: 'POST',
    headers: { ...authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify(quizPayload)
  });
  const { quiz } = await quizResp.json();
  log(`✅ Created quiz id=${quiz.id}`);

  // 4️⃣ Create a session for the class
  log('🎮 Creating session');
  const sessionResp = await fetch(`${backend}/api/teacher/sessions`, {
    method: 'POST',
    headers: { ...authHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({ classId: createdClass.id })
  });
  const { session } = await sessionResp.json();
  log(`✅ Session code=${session.code}`);

  // 5️⃣ Simulate a Unity client: fetch public quiz and send attempt
  log('🕹️ Simulating Unity client');
  const publicCatalogResp = await fetch(`${backend}/api/public/catalog?classId=${createdClass.id}`);
  const publicCatalog = await publicCatalogResp.json();
  const publicQuiz = publicCatalog.subjects.flatMap(s => s.quizzes).find(q => q.id === quiz.id);
  if (!publicQuiz) throw new Error('Quiz not found in public catalog');
  log('✅ Retrieved public quiz');

  // Build answers – 3 correct, 1 wrong => 75% => 2 stars
  const answers = [];
  for (let i = 0; i < publicQuiz.questions.length; i++) {
    const q = publicQuiz.questions[i];
    const correctOption = q.options.find(o => o.id === q.correctOptionId);
    const wrongOption = q.options.find(o => o.id !== q.correctOptionId);
    const optionId = i < 3 ? correctOption.id : wrongOption.id; // first three correct
    answers.push({
      questionId: q.id,
      optionId,
      correct: i < 3,
      answeredAt: Date.now()
    });
  }

  const attemptPayload = {
    id: `attempt-${Date.now()}`,
    code: session.code,
    playerId: `player-${Date.now()}`,
    name: 'Aluno Teste',
    avatarId: 'avatar-1',
    quizId: quiz.id,
    startedAt: Date.now() - 5000,
    completedAt: Date.now(),
    score: answers.filter(a => a.correct).length,
    total: answers.length,
    answers
  };

  const attemptResp = await fetch(`${backend}/api/public/attempt`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(attemptPayload)
  });
  if (!attemptResp.ok) {
    const txt = await attemptResp.text();
    throw new Error(`Attempt upload failed: ${attemptResp.status} ${txt}`);
  }
  log('✅ Attempt uploaded');

  // 6️⃣ Verify results via teacher endpoint
  const resultsResp = await fetch(`${backend}/api/teacher/results`, { headers: authHeader });
  const { results } = await resultsResp.json();
  const myResult = results.find(r => r.playerName === 'Aluno Teste' && r.quizTitle === quiz.title);
  if (!myResult) throw new Error('Result not found for test player');
  log(`✅ Result found – score=${myResult.score}/${myResult.total}`);

  const percent = myResult.score / myResult.total;
  const expectedStars = percent === 1 ? 3 : percent >= 0.6 ? 2 : percent > 0 ? 1 : 0;
  log(`💫 Expected stars=${expectedStars}`);

  // Verify player_progress (new endpoint added for debugging)
  const progResp = await fetch(`${backend}/api/teacher/progress?quizId=${quiz.id}`, { headers: authHeader });
  if (progResp.ok) {
    const { progress } = await progResp.json();
    const pp = progress.find(p => p.playerId === attemptPayload.playerId);
    if (pp) log(`✅ player_progress shows bestStars=${pp.bestStars}`);
  }

  log('🏁 Integration test completed');
}

main().catch(err => { log(`❌ Test failed: ${err.message}`); process.exit(1); });

