import { randomBytes } from 'node:crypto';

const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const tokens = new Map();
const tokenFor = teacher => {
  const token = randomBytes(32).toString('base64url');
  tokens.set(token, { teacher, expiresAt: Date.now() + 8 * 60 * 60_000 });
  return token;
};
const error = (status, code, message) => Object.assign(new Error(message), { status, code });

function auth(req) {
  const match = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(req.headers.authorization || '');
  const session = match && tokens.get(match[1]);
  if (!session || session.expiresAt < Date.now()) {
    if (match) tokens.delete(match[1]);
    throw error(401, 'teacher_unauthorized', 'Entre como professora ou professor para continuar.');
  }
  return session.teacher;
}

function createCode(database) {
  for (let attempt = 0; attempt < 50; attempt++) {
    const code = Array.from(randomBytes(6), byte => ALPHABET[byte % ALPHABET.length]).join('');
    if (!database.session(code)) return code;
  }
  throw error(503, 'session_code_unavailable', 'Não foi possível criar um código de sessão agora.');
}

/** Rotas privadas da autoria. A web só conhece estas rotas, nunca o SQLite diretamente. */
export function createTeacherApi(database) {
  return {
    async handle({ req, method, path, readJson }) {
      const body = async () => readJson(req);
      if (method === 'POST' && path === '/api/teacher/auth/register') {
        const teacher = database.registerTeacher(await body());
        return { status: 201, body: { token: tokenFor(teacher), teacher } };
      }
      if (method === 'POST' && path === '/api/teacher/auth/login') {
        const input = await body();
        const teacher = database.authenticateTeacher(input.email, input.password);
        if (!teacher) throw error(401, 'invalid_credentials', 'E-mail ou senha não conferem.');
        return { status: 200, body: { token: tokenFor(teacher), teacher } };
      }
      if (!path.startsWith('/api/teacher/')) return null;
      const teacher = auth(req);
      if (method === 'POST' && path === '/api/teacher/auth/logout') {
        const match = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(req.headers.authorization || '');
        if (match) tokens.delete(match[1]);
        return { status: 200, body: { ok: true } };
      }
      if (method === 'GET' && path === '/api/teacher/me') return { status: 200, body: { teacher } };
      if (method === 'GET' && path === '/api/teacher/dashboard') return { status: 200, body: database.dashboard(teacher.id) };
      if (method === 'GET' && path === '/api/teacher/subjects') return { status: 200, body: { subjects: database.subjects() } };
      if (method === 'POST' && path === '/api/teacher/subjects') return { status: 201, body: { subject: database.createSubject(teacher.id, await body()) } };
      if (method === 'GET' && path === '/api/teacher/classes') return { status: 200, body: { classes: database.classesFor(teacher.id) } };
      if (method === 'POST' && path === '/api/teacher/classes') return { status: 201, body: { class: database.createClass(teacher.id, await body()) } };
      if (method === 'GET' && path === '/api/teacher/quizzes') return { status: 200, body: { quizzes: database.quizzesFor(teacher.id) } };
      if (method === 'POST' && path === '/api/teacher/quizzes') return { status: 201, body: { quiz: database.createQuiz(teacher.id, await body()) } };
      if (method === 'GET' && path === '/api/teacher/results') return { status: 200, body: { results: database.resultsFor(teacher.id) } };
      if (method === 'POST' && path === '/api/teacher/sessions') {
        const input = await body();
        if (!input.code) input.code = createCode(database);
        return { status: 201, body: { session: database.createGameSession(teacher.id, input) } };
      }
      throw error(404, 'teacher_route_not_found', 'Essa área do portal não existe.');
    }
  };
}
