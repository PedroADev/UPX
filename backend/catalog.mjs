import { readFileSync } from 'node:fs';

// A mesma fonte alimenta a prática local da Unity. A API publica só os enunciados.
export const catalog = JSON.parse(readFileSync(new URL('../Assets/Aprender/Resources/quiz-catalog.json', import.meta.url), 'utf8'));

export function validateCatalog(source) {
  const ids = new Set();
  const takeId = (id) => {
    if (typeof id !== 'string' || !/^[a-z0-9-]+$/.test(id) || ids.has(id)) throw new Error(`ID de catálogo inválido ou repetido: ${id}`);
    ids.add(id);
  };
  if (!Array.isArray(source.subjects) || !source.subjects.length) throw new Error('Catálogo sem disciplinas.');
  for (const subject of source.subjects) {
    takeId(subject.id);
    if (!subject.title || !subject.quizzes?.length) throw new Error('Disciplina incompleta.');
    for (const quiz of subject.quizzes) {
      takeId(quiz.id);
      if (!quiz.title || !quiz.questions?.length) throw new Error('Quiz incompleto.');
      for (const question of quiz.questions) {
        takeId(question.id);
        if (!question.text || question.options?.length < 2 || !question.explanation) throw new Error('Questão incompleta.');
        const options = new Set(question.options.map(option => option.id));
        if (options.size !== question.options.length || !options.has(question.correctOptionId)) throw new Error(`Alternativa correta inválida: ${question.id}`);
        if (question.options.some(option => !option.text || typeof option.id !== 'string')) throw new Error('Alternativa incompleta.');
      }
    }
  }
}

export function publicCatalog(source) {
  return {
    subjects: source.subjects.map(subject => ({
      id: subject.id, title: subject.title, subtitle: subject.subtitle, color: subject.color, icon: subject.icon,
      quizzes: subject.quizzes.map(quiz => ({
        id: quiz.id, title: quiz.title, description: quiz.description,
        questions: quiz.questions.map(question => ({
          id: question.id, text: question.text,
          options: question.options.map(option => ({ id: option.id, text: option.text }))
        }))
      }))
    }))
  };
}

validateCatalog(catalog);
