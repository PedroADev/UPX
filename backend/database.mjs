import { DatabaseSync } from 'node:sqlite';
import { randomBytes, randomUUID, scryptSync, timingSafeEqual } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';

const now = () => Date.now();
const id = prefix => `${prefix}-${randomUUID()}`;

function cleanText(value, minimum, maximum, label) {
  const text = typeof value === 'string' ? value.trim().replace(/\s+/g, ' ') : '';
  if (Array.from(text).length < minimum || Array.from(text).length > maximum) throw new Error(`${label} deve ter de ${minimum} a ${maximum} caracteres.`);
  return text;
}

function passwordRecord(password) {
  if (typeof password !== 'string' || password.length < 8 || password.length > 128) throw new Error('A senha deve ter entre 8 e 128 caracteres.');
  const salt = randomBytes(16).toString('hex');
  return { salt, hash: scryptSync(password, salt, 64).toString('hex') };
}

function isPasswordValid(password, row) {
  if (!row || typeof password !== 'string') return false;
  const actual = Buffer.from(scryptSync(password, row.password_salt, 64).toString('hex'), 'hex');
  const expected = Buffer.from(row.password_hash, 'hex');
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

/** Uma fonte de verdade local para o portal e para a API que a Unity consome. */
export class AprenderDatabase {
  constructor(file, seedCatalog) {
    mkdirSync(dirname(file), { recursive: true });
    this.db = new DatabaseSync(file);
    this.db.exec('PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;');
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS teachers (
        id TEXT PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL UNIQUE,
        password_hash TEXT NOT NULL, password_salt TEXT NOT NULL, created_at INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS subjects (
        id TEXT PRIMARY KEY, title TEXT NOT NULL, subtitle TEXT NOT NULL DEFAULT '',
        color TEXT NOT NULL DEFAULT '#4F46E5', icon TEXT NOT NULL DEFAULT 'book', created_at INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS classes (
        id TEXT PRIMARY KEY, teacher_id TEXT NOT NULL REFERENCES teachers(id) ON DELETE CASCADE,
        name TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', created_at INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS quizzes (
        id TEXT PRIMARY KEY, teacher_id TEXT NOT NULL REFERENCES teachers(id) ON DELETE CASCADE,
        subject_id TEXT NOT NULL REFERENCES subjects(id), class_id TEXT REFERENCES classes(id) ON DELETE SET NULL,
        title TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', status TEXT NOT NULL CHECK(status IN ('draft','published')),
        created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS questions (
        id TEXT PRIMARY KEY, quiz_id TEXT NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
        position INTEGER NOT NULL, text TEXT NOT NULL, explanation TEXT NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS options (
        id TEXT PRIMARY KEY, question_id TEXT NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
        position INTEGER NOT NULL, text TEXT NOT NULL, is_correct INTEGER NOT NULL CHECK(is_correct IN (0,1))
      ) STRICT;
      CREATE TABLE IF NOT EXISTS game_sessions (
        code TEXT PRIMARY KEY, teacher_id TEXT REFERENCES teachers(id) ON DELETE SET NULL,
        class_id TEXT REFERENCES classes(id) ON DELETE SET NULL, created_at INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS attempts (
        id TEXT PRIMARY KEY, session_code TEXT NOT NULL, player_id TEXT NOT NULL, player_name TEXT NOT NULL,
        avatar_id TEXT NOT NULL, quiz_id TEXT NOT NULL REFERENCES quizzes(id), started_at INTEGER NOT NULL,
        completed_at INTEGER NOT NULL, score INTEGER NOT NULL, total INTEGER NOT NULL
      ) STRICT;
      CREATE TABLE IF NOT EXISTS attempt_answers (
        attempt_id TEXT NOT NULL REFERENCES attempts(id) ON DELETE CASCADE, question_id TEXT NOT NULL,
        option_id TEXT NOT NULL, correct INTEGER NOT NULL CHECK(correct IN (0,1)), answered_at INTEGER NOT NULL,
        PRIMARY KEY(attempt_id, question_id)
      ) STRICT;
      CREATE INDEX IF NOT EXISTS idx_quizzes_catalog ON quizzes(status, subject_id, class_id);
      CREATE INDEX IF NOT EXISTS idx_attempts_quiz ON attempts(quiz_id, completed_at DESC);
    `);
    this.seed(seedCatalog);
  }

  close() { this.db.close(); }

  seed(catalog) {
    const hasSubjects = this.db.prepare('SELECT 1 FROM subjects LIMIT 1').get();
    if (hasSubjects) return;
    const createdAt = now();
    this.db.exec('BEGIN IMMEDIATE');
    try {
      for (const subject of catalog.subjects) {
        this.db.prepare('INSERT INTO subjects (id,title,subtitle,color,icon,created_at) VALUES (?,?,?,?,?,?)')
          .run(subject.id, subject.title, subject.subtitle || '', subject.color || '#4F46E5', subject.icon || 'book', createdAt);
      }
      const teacher = passwordRecord('IlhaDoSaber!2026');
      this.db.prepare('INSERT INTO teachers (id,name,email,password_hash,password_salt,created_at) VALUES (?,?,?,?,?,?)')
        .run('teacher-demo', 'Beatriz Souza', 'beatriz@ilhasaber.local', teacher.hash, teacher.salt, createdAt);
      for (const subject of catalog.subjects) for (const quiz of subject.quizzes) {
        this.db.prepare('INSERT INTO quizzes (id,teacher_id,subject_id,class_id,title,description,status,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?)')
          .run(quiz.id, 'teacher-demo', subject.id, null, quiz.title, quiz.description || '', 'published', createdAt, createdAt);
        quiz.questions.forEach((question, position) => {
          this.db.prepare('INSERT INTO questions (id,quiz_id,position,text,explanation) VALUES (?,?,?,?,?)')
            .run(question.id, quiz.id, position, question.text, question.explanation);
          question.options.forEach((option, optionPosition) => this.db.prepare('INSERT INTO options (id,question_id,position,text,is_correct) VALUES (?,?,?,?,?)')
            .run(`${question.id}-${option.id}`, question.id, optionPosition, option.text, option.id === question.correctOptionId ? 1 : 0));
        });
      }
      this.db.exec('COMMIT');
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }

  catalog(classId = null) {
    const subjects = this.db.prepare('SELECT id,title,subtitle,color,icon FROM subjects ORDER BY title').all();
    const all = [];
    for (const subject of subjects) {
      const quizRows = this.db.prepare(`SELECT id,title,description FROM quizzes
        WHERE subject_id=? AND status='published' AND (class_id IS NULL OR class_id=?) ORDER BY created_at`).all(subject.id, classId);
      const quizzes = quizRows.map(quiz => ({ ...quiz, questions: this.questionsForQuiz(quiz.id) }));
      if (quizzes.length) all.push({ ...subject, quizzes });
    }
    return { subjects: all };
  }

  questionsForQuiz(quizId) {
    return this.db.prepare('SELECT id,text,explanation FROM questions WHERE quiz_id=? ORDER BY position').all(quizId).map(question => {
      const options = this.db.prepare('SELECT id,text,is_correct FROM options WHERE question_id=? ORDER BY position').all(question.id);
      return { id: question.id, text: question.text, explanation: question.explanation,
        options: options.map(option => ({ id: option.id, text: option.text })),
        correctOptionId: options.find(option => option.is_correct === 1)?.id };
    });
  }

  registerTeacher({ name, email, password }) {
    const cleanEmail = typeof email === 'string' ? email.trim().toLowerCase() : '';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(cleanEmail) || cleanEmail.length > 120) throw new Error('Use um e-mail válido.');
    const credentials = passwordRecord(password);
    const teacher = { id: id('teacher'), name: cleanText(name, 2, 60, 'O nome'), email: cleanEmail, createdAt: now() };
    try { this.db.prepare('INSERT INTO teachers (id,name,email,password_hash,password_salt,created_at) VALUES (?,?,?,?,?,?)').run(teacher.id, teacher.name, teacher.email, credentials.hash, credentials.salt, teacher.createdAt); }
    catch (error) { if (/UNIQUE/.test(error.message)) throw new Error('Já existe uma conta com esse e-mail.'); throw error; }
    return teacher;
  }

  authenticateTeacher(email, password) {
    const row = this.db.prepare('SELECT * FROM teachers WHERE email=?').get(typeof email === 'string' ? email.trim().toLowerCase() : '');
    if (!isPasswordValid(password, row)) return null;
    return { id: row.id, name: row.name, email: row.email };
  }

  subjects() { return this.db.prepare('SELECT id,title,subtitle,color,icon FROM subjects ORDER BY title').all(); }

  createSubject(teacherId, data) {
    const title = cleanText(data.title, 2, 40, 'O nome da disciplina');
    const slug = title.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 32);
    let subjectId = slug || id('subject'); let suffix = 2;
    while (this.db.prepare('SELECT 1 FROM subjects WHERE id=?').get(subjectId)) subjectId = `${slug}-${suffix++}`;
    const subject = { id: subjectId, title, subtitle: typeof data.subtitle === 'string' ? data.subtitle.trim().slice(0, 90) : '', color: /^#[0-9a-f]{6}$/i.test(data.color || '') ? data.color : '#4F46E5', icon: typeof data.icon === 'string' ? data.icon.slice(0, 24) : 'book' };
    this.db.prepare('INSERT INTO subjects (id,title,subtitle,color,icon,created_at) VALUES (?,?,?,?,?,?)').run(subject.id, subject.title, subject.subtitle, subject.color, subject.icon, now());
    return subject;
  }

  classesFor(teacherId) { return this.db.prepare('SELECT id,name,description,created_at AS createdAt FROM classes WHERE teacher_id=? ORDER BY created_at DESC').all(teacherId); }
  createClass(teacherId, data) {
    const item = { id: id('class'), name: cleanText(data.name, 2, 60, 'O nome da turma'), description: typeof data.description === 'string' ? data.description.trim().slice(0, 180) : '', createdAt: now() };
    this.db.prepare('INSERT INTO classes (id,teacher_id,name,description,created_at) VALUES (?,?,?,?,?)').run(item.id, teacherId, item.name, item.description, item.createdAt);
    return item;
  }

  quizzesFor(teacherId) {
    return this.db.prepare(`SELECT q.id,q.title,q.description,q.status,q.subject_id AS subjectId,q.class_id AS classId,q.created_at AS createdAt,
      s.title AS subjectTitle,c.name AS className,COUNT(question.id) AS questionCount
      FROM quizzes q JOIN subjects s ON s.id=q.subject_id LEFT JOIN classes c ON c.id=q.class_id LEFT JOIN questions question ON question.quiz_id=q.id
      WHERE q.teacher_id=? GROUP BY q.id ORDER BY q.updated_at DESC`).all(teacherId);
  }

  createQuiz(teacherId, data) {
    const title = cleanText(data.title, 3, 80, 'O título do quiz');
    if (!this.db.prepare('SELECT 1 FROM subjects WHERE id=?').get(data.subjectId)) throw new Error('Escolha uma disciplina existente.');
    const classId = data.classId || null;
    if (classId && !this.db.prepare('SELECT 1 FROM classes WHERE id=? AND teacher_id=?').get(classId, teacherId)) throw new Error('A turma escolhida não pertence à sua conta.');
    if (!Array.isArray(data.questions) || data.questions.length < 1 || data.questions.length > 20) throw new Error('O quiz precisa ter de 1 a 20 questões.');
    const questions = data.questions.map((question, position) => {
      const text = cleanText(question.text, 4, 280, `A questão ${position + 1}`);
      const explanation = cleanText(question.explanation || 'Continue praticando!', 3, 280, `A explicação da questão ${position + 1}`);
      if (!Array.isArray(question.options) || question.options.length < 2 || question.options.length > 5) throw new Error(`A questão ${position + 1} precisa ter de 2 a 5 alternativas.`);
      const options = question.options.map((option, optionPosition) => ({ text: cleanText(option.text, 1, 160, `A alternativa ${optionPosition + 1}`), isCorrect: option.isCorrect === true }));
      if (options.filter(option => option.isCorrect).length !== 1) throw new Error(`Marque uma única resposta correta na questão ${position + 1}.`);
      return { text, explanation, options };
    });
    const quiz = { id: id('quiz'), title, description: typeof data.description === 'string' ? data.description.trim().slice(0, 240) : '', subjectId: data.subjectId, classId, status: data.status === 'draft' ? 'draft' : 'published', createdAt: now() };
    this.db.exec('BEGIN IMMEDIATE');
    try {
      this.db.prepare('INSERT INTO quizzes (id,teacher_id,subject_id,class_id,title,description,status,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?)').run(quiz.id, teacherId, quiz.subjectId, quiz.classId, quiz.title, quiz.description, quiz.status, quiz.createdAt, quiz.createdAt);
      questions.forEach((question, position) => {
        const questionId = id('question');
        this.db.prepare('INSERT INTO questions (id,quiz_id,position,text,explanation) VALUES (?,?,?,?,?)').run(questionId, quiz.id, position, question.text, question.explanation);
        question.options.forEach((option, optionPosition) => this.db.prepare('INSERT INTO options (id,question_id,position,text,is_correct) VALUES (?,?,?,?,?)').run(id('option'), questionId, optionPosition, option.text, option.isCorrect ? 1 : 0));
      });
      this.db.exec('COMMIT');
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
    return quiz;
  }

  createGameSession(teacherId, data) {
    const classRow = this.db.prepare('SELECT id FROM classes WHERE id=? AND teacher_id=?').get(data.classId, teacherId);
    if (!classRow) throw new Error('Escolha uma turma sua para abrir a sessão.');
    const code = typeof data.code === 'string' ? data.code.trim().toUpperCase() : '';
    if (!/^[A-Z0-9]{4,8}$/.test(code)) throw new Error('O código precisa ter de 4 a 8 letras ou números.');
    try { this.db.prepare('INSERT INTO game_sessions (code,teacher_id,class_id,created_at) VALUES (?,?,?,?)').run(code, teacherId, classRow.id, now()); }
    catch (error) { if (/UNIQUE/.test(error.message)) throw new Error('Esse código já foi usado. Escolha outro.'); throw error; }
    return { code, classId: classRow.id };
  }

  session(code) { return this.db.prepare('SELECT code,class_id AS classId FROM game_sessions WHERE code=?').get(code) || null; }
  saveAttempt(record) {
    this.db.exec('BEGIN IMMEDIATE');
    try {
      this.db.prepare('INSERT INTO attempts (id,session_code,player_id,player_name,avatar_id,quiz_id,started_at,completed_at,score,total) VALUES (?,?,?,?,?,?,?,?,?,?)')
        .run(record.id, record.code, record.playerId, record.name, record.avatarId, record.quizId, record.startedAt, record.completedAt, record.score, record.total);
      const statement = this.db.prepare('INSERT INTO attempt_answers (attempt_id,question_id,option_id,correct,answered_at) VALUES (?,?,?,?,?)');
      record.answers.forEach(answer => statement.run(record.id, answer.questionId, answer.optionId, answer.correct ? 1 : 0, answer.answeredAt));
      this.db.exec('COMMIT');
    } catch (error) { this.db.exec('ROLLBACK'); throw error; }
  }

  dashboard(teacherId) {
    const scalar = sql => this.db.prepare(sql).get(teacherId).total;
    return { activeQuizzes: scalar("SELECT COUNT(*) AS total FROM quizzes WHERE teacher_id=? AND status='published'"), questions: scalar('SELECT COUNT(*) AS total FROM questions q JOIN quizzes z ON z.id=q.quiz_id WHERE z.teacher_id=?'), classes: scalar('SELECT COUNT(*) AS total FROM classes WHERE teacher_id=?'), sessions: scalar('SELECT COUNT(*) AS total FROM game_sessions WHERE teacher_id=?') };
  }
  resultsFor(teacherId) { return this.db.prepare(`SELECT a.player_name AS playerName,a.score,a.total,a.completed_at AS completedAt,q.title AS quizTitle,c.name AS className
    FROM attempts a JOIN quizzes q ON q.id=a.quiz_id LEFT JOIN classes c ON c.id=q.class_id WHERE q.teacher_id=? ORDER BY a.completed_at DESC LIMIT 20`).all(teacherId); }
}

export function openDatabase(file, seedCatalog) { return new AprenderDatabase(file, seedCatalog); }
