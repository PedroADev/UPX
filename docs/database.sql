-- Proposta PostgreSQL 15+ para o futuro portal dos professores.
-- Não é uma migração do protótipo Node: hoje ele usa memória + histórico JSON.
-- Mantém os conceitos do desenho e acrescenta versões de quiz, matrícula,
-- sessão, presença e resposta individual. Execute em um banco vazio.
BEGIN;

CREATE TABLE professor (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nome text NOT NULL CHECK (char_length(trim(nome)) BETWEEN 2 AND 120),
    email text NOT NULL,
    senha_hash text, -- somente hash Argon2id/bcrypt; nunca senha em texto
    identidade_externa text UNIQUE,
    criado_em timestamptz NOT NULL DEFAULT now(),
    CHECK (senha_hash IS NOT NULL OR identidade_externa IS NOT NULL)
);
CREATE UNIQUE INDEX professor_email_unico ON professor (lower(email));

CREATE TABLE turma (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    professor_id uuid NOT NULL REFERENCES professor(id),
    nome text NOT NULL CHECK (char_length(trim(nome)) BETWEEN 2 AND 100),
    descricao text NOT NULL DEFAULT '',
    criada_em timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE aluno (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nome_exibicao text NOT NULL CHECK (char_length(trim(nome_exibicao)) BETWEEN 2 AND 20),
    criado_em timestamptz NOT NULL DEFAULT now()
    -- A criança entra por código de sessão; não precisa de e-mail ou senha.
);

CREATE TABLE matricula (
    turma_id uuid NOT NULL REFERENCES turma(id),
    aluno_id uuid NOT NULL REFERENCES aluno(id),
    criada_em timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (turma_id, aluno_id)
    -- Permite que um aluno participe de mais de uma turma.
);

CREATE TABLE disciplina (
    id text PRIMARY KEY CHECK (id ~ '^[a-z0-9-]+$'),
    nome text NOT NULL,
    descricao text NOT NULL DEFAULT '',
    cor text NOT NULL DEFAULT '#58CFA8' CHECK (cor ~ '^#[0-9A-Fa-f]{6}$')
);

CREATE TABLE sala (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    turma_id uuid NOT NULL REFERENCES turma(id),
    disciplina_id text NOT NULL REFERENCES disciplina(id),
    nome text NOT NULL,
    descricao text NOT NULL DEFAULT '',
    UNIQUE (id, turma_id),
    UNIQUE (id, disciplina_id),
    UNIQUE (turma_id, disciplina_id)
);

CREATE TABLE quiz (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    professor_id uuid NOT NULL REFERENCES professor(id),
    disciplina_id text NOT NULL REFERENCES disciplina(id),
    titulo text NOT NULL CHECK (char_length(trim(titulo)) BETWEEN 1 AND 160),
    criado_em timestamptz NOT NULL DEFAULT now(),
    UNIQUE (id, disciplina_id)
);

CREATE TABLE quiz_versao (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    quiz_id uuid NOT NULL,
    disciplina_id text NOT NULL,
    numero integer NOT NULL CHECK (numero > 0),
    titulo text NOT NULL,
    descricao text NOT NULL DEFAULT '',
    estado text NOT NULL DEFAULT 'rascunho' CHECK (estado IN ('rascunho', 'publicado', 'arquivado')),
    criada_em timestamptz NOT NULL DEFAULT now(),
    publicada_em timestamptz,
    FOREIGN KEY (quiz_id, disciplina_id) REFERENCES quiz(id, disciplina_id),
    UNIQUE (quiz_id, numero),
    UNIQUE (id, disciplina_id),
    CHECK (estado = 'rascunho' OR publicada_em IS NOT NULL)
    -- Questões de uma versão publicada devem ser imutáveis; uma edição cria nova versão.
);

CREATE TABLE questao (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    quiz_versao_id uuid NOT NULL REFERENCES quiz_versao(id),
    ordem integer NOT NULL CHECK (ordem >= 0),
    texto text NOT NULL CHECK (char_length(trim(texto)) > 0),
    explicacao text NOT NULL CHECK (char_length(trim(explicacao)) > 0),
    UNIQUE (id, quiz_versao_id),
    UNIQUE (quiz_versao_id, ordem)
);

CREATE TABLE alternativa (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    questao_id uuid NOT NULL REFERENCES questao(id),
    ordem integer NOT NULL CHECK (ordem >= 0),
    texto text NOT NULL CHECK (char_length(trim(texto)) > 0),
    correta boolean NOT NULL DEFAULT false,
    pontuacao integer NOT NULL DEFAULT 0,
    CHECK ((correta AND pontuacao > 0) OR (NOT correta AND pontuacao = 0)),
    UNIQUE (id, questao_id),
    UNIQUE (questao_id, ordem)
);
-- No máximo uma correta. O gatilho de publicação exige exatamente uma.
CREATE UNIQUE INDEX alternativa_uma_correta ON alternativa (questao_id) WHERE correta;

CREATE TABLE sala_quiz (
    sala_id uuid NOT NULL,
    quiz_versao_id uuid NOT NULL,
    disciplina_id text NOT NULL,
    PRIMARY KEY (sala_id, quiz_versao_id),
    FOREIGN KEY (sala_id, disciplina_id) REFERENCES sala(id, disciplina_id),
    FOREIGN KEY (quiz_versao_id, disciplina_id) REFERENCES quiz_versao(id, disciplina_id)
);

CREATE TABLE sessao (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    turma_id uuid NOT NULL REFERENCES turma(id),
    criada_por uuid NOT NULL REFERENCES professor(id),
    codigo text NOT NULL CHECK (codigo ~ '^[A-Z0-9]{4,8}$'),
    criada_em timestamptz NOT NULL DEFAULT now(),
    encerrada_em timestamptz,
    expira_em timestamptz NOT NULL,
    UNIQUE (id, turma_id),
    CHECK (expira_em > criada_em),
    CHECK (encerrada_em IS NULL OR encerrada_em >= criada_em)
);
CREATE UNIQUE INDEX sessao_codigo_ativo ON sessao (codigo) WHERE encerrada_em IS NULL;

CREATE TABLE participacao (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sessao_id uuid NOT NULL,
    turma_id uuid NOT NULL,
    aluno_id uuid NOT NULL,
    nome_exibicao text NOT NULL CHECK (char_length(trim(nome_exibicao)) BETWEEN 2 AND 20),
    avatar_id text NOT NULL CHECK (avatar_id ~ '^[A-Za-z0-9_-]{1,48}$'),
    token_hash bytea, -- se persistido, só SHA-256 do token aleatório de alta entropia
    entrou_em timestamptz NOT NULL DEFAULT now(),
    saiu_em timestamptz,
    FOREIGN KEY (sessao_id, turma_id) REFERENCES sessao(id, turma_id),
    FOREIGN KEY (turma_id, aluno_id) REFERENCES matricula(turma_id, aluno_id),
    UNIQUE (id, turma_id),
    CHECK (saiu_em IS NULL OR saiu_em >= entrou_em)
);
CREATE UNIQUE INDEX participacao_token_unico ON participacao(token_hash) WHERE token_hash IS NOT NULL;
CREATE UNIQUE INDEX participacao_aluno_ativo ON participacao(sessao_id, aluno_id) WHERE saiu_em IS NULL;

CREATE TABLE presenca (
    participacao_id uuid PRIMARY KEY,
    turma_id uuid NOT NULL,
    sala_id uuid, -- NULL representa o pátio/hub
    quiz_versao_id uuid,
    x double precision NOT NULL DEFAULT 0 CHECK (x BETWEEN -1000 AND 1000),
    z double precision NOT NULL DEFAULT 0 CHECK (z BETWEEN -1000 AND 1000),
    yaw double precision NOT NULL DEFAULT 0 CHECK (yaw >= 0 AND yaw < 360),
    vista_em timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (participacao_id, turma_id) REFERENCES participacao(id, turma_id),
    FOREIGN KEY (sala_id, turma_id) REFERENCES sala(id, turma_id),
    FOREIGN KEY (sala_id, quiz_versao_id) REFERENCES sala_quiz(sala_id, quiz_versao_id),
    CHECK (quiz_versao_id IS NULL OR sala_id IS NOT NULL)
);
CREATE INDEX presenca_recente ON presenca(vista_em);

CREATE TABLE tentativa (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    participacao_id uuid NOT NULL,
    turma_id uuid NOT NULL,
    sala_id uuid NOT NULL,
    quiz_versao_id uuid NOT NULL,
    iniciada_em timestamptz NOT NULL DEFAULT now(),
    concluida_em timestamptz,
    abandonada_em timestamptz,
    FOREIGN KEY (participacao_id, turma_id) REFERENCES participacao(id, turma_id),
    FOREIGN KEY (sala_id, turma_id) REFERENCES sala(id, turma_id),
    FOREIGN KEY (sala_id, quiz_versao_id) REFERENCES sala_quiz(sala_id, quiz_versao_id),
    UNIQUE (id, quiz_versao_id),
    CHECK (concluida_em IS NULL OR concluida_em >= iniciada_em),
    CHECK (abandonada_em IS NULL OR abandonada_em >= iniciada_em),
    CHECK (concluida_em IS NULL OR abandonada_em IS NULL)
);
CREATE INDEX tentativa_por_aluno ON tentativa(participacao_id, iniciada_em DESC);
CREATE INDEX tentativa_por_quiz ON tentativa(quiz_versao_id, iniciada_em DESC);
CREATE UNIQUE INDEX tentativa_uma_em_andamento ON tentativa(participacao_id)
    WHERE concluida_em IS NULL AND abandonada_em IS NULL;

CREATE TABLE resposta (
    tentativa_id uuid NOT NULL,
    quiz_versao_id uuid NOT NULL,
    questao_id uuid NOT NULL,
    alternativa_id uuid NOT NULL,
    respondida_em timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tentativa_id, questao_id),
    FOREIGN KEY (tentativa_id, quiz_versao_id) REFERENCES tentativa(id, quiz_versao_id),
    FOREIGN KEY (questao_id, quiz_versao_id) REFERENCES questao(id, quiz_versao_id),
    FOREIGN KEY (alternativa_id, questao_id) REFERENCES alternativa(id, questao_id)
    -- FKs compostas impedem responder questão de outro quiz ou alternativa de outra questão.
);

CREATE VIEW tentativa_resultado AS
SELECT t.id, t.participacao_id, t.quiz_versao_id, t.iniciada_em, t.concluida_em,
       count(r.questao_id)::integer AS respondidas,
       coalesce(sum(a.pontuacao), 0)::integer AS pontuacao,
       count(*) FILTER (WHERE a.correta)::integer AS acertos,
       (SELECT count(*)::integer FROM questao q WHERE q.quiz_versao_id = t.quiz_versao_id) AS total
FROM tentativa t
LEFT JOIN resposta r ON r.tentativa_id = t.id
LEFT JOIN alternativa a ON a.id = r.alternativa_id
GROUP BY t.id;

CREATE FUNCTION validar_publicacao_quiz() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.estado <> 'rascunho' AND (NEW.quiz_id, NEW.disciplina_id, NEW.numero, NEW.titulo, NEW.descricao)
       IS DISTINCT FROM (OLD.quiz_id, OLD.disciplina_id, OLD.numero, OLD.titulo, OLD.descricao) THEN
        RAISE EXCEPTION 'Crie nova versão para editar um quiz publicado';
    END IF;
    IF OLD.estado <> 'rascunho' AND NEW.estado = 'rascunho' THEN
        RAISE EXCEPTION 'Uma versão publicada não pode voltar a rascunho';
    END IF;
    IF NEW.estado = 'publicado' THEN
        IF NOT EXISTS (SELECT 1 FROM questao WHERE quiz_versao_id = NEW.id) THEN
            RAISE EXCEPTION 'Publique pelo menos uma questão';
        END IF;
        IF EXISTS (
            SELECT q.id FROM questao q LEFT JOIN alternativa a ON a.questao_id = q.id
            WHERE q.quiz_versao_id = NEW.id GROUP BY q.id
            HAVING count(a.id) < 2 OR count(a.id) FILTER (WHERE a.correta) <> 1
        ) THEN
            RAISE EXCEPTION 'Cada questão precisa de duas alternativas e exatamente uma correta';
        END IF;
        NEW.publicada_em := coalesce(NEW.publicada_em, now());
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER quiz_publicacao BEFORE UPDATE ON quiz_versao
FOR EACH ROW EXECUTE FUNCTION validar_publicacao_quiz();

CREATE FUNCTION proteger_conteudo_publicado() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE versao_anterior uuid; versao_nova uuid;
BEGIN
    IF TG_TABLE_NAME = 'questao' THEN
        IF TG_OP <> 'INSERT' THEN versao_anterior := OLD.quiz_versao_id; END IF;
        IF TG_OP <> 'DELETE' THEN versao_nova := NEW.quiz_versao_id; END IF;
    ELSE
        IF TG_OP <> 'INSERT' THEN SELECT quiz_versao_id INTO versao_anterior FROM questao WHERE id = OLD.questao_id; END IF;
        IF TG_OP <> 'DELETE' THEN SELECT quiz_versao_id INTO versao_nova FROM questao WHERE id = NEW.questao_id; END IF;
    END IF;
    -- Publicação e edição do conteúdo tomam o mesmo bloqueio de versão, evitando
    -- que uma edição de rascunho atravesse a publicação em outra transação.
    PERFORM id FROM quiz_versao WHERE id IN (versao_anterior, versao_nova) ORDER BY id FOR UPDATE;
    IF EXISTS (SELECT 1 FROM quiz_versao WHERE id IN (versao_anterior, versao_nova) AND estado <> 'rascunho') THEN
        RAISE EXCEPTION 'O conteúdo publicado é imutável; crie nova versão';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER questao_imutavel BEFORE INSERT OR UPDATE OR DELETE ON questao
FOR EACH ROW EXECUTE FUNCTION proteger_conteudo_publicado();
CREATE TRIGGER alternativa_imutavel BEFORE INSERT OR UPDATE OR DELETE ON alternativa
FOR EACH ROW EXECUTE FUNCTION proteger_conteudo_publicado();

-- Publicar somente por UPDATE de um rascunho existente, nunca INSERT publicado.
CREATE FUNCTION exigir_rascunho_inicial() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.estado <> 'rascunho' THEN RAISE EXCEPTION 'Crie a versão como rascunho antes de publicar'; END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER quiz_rascunho_inicial BEFORE INSERT ON quiz_versao
FOR EACH ROW EXECUTE FUNCTION exigir_rascunho_inicial();

CREATE FUNCTION exigir_quiz_publicado() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE estado_atual text;
BEGIN
    IF TG_OP = 'UPDATE' THEN
        IF NEW.quiz_versao_id = OLD.quiz_versao_id THEN RETURN NEW; END IF;
    END IF;
    SELECT estado INTO estado_atual FROM quiz_versao WHERE id = NEW.quiz_versao_id FOR SHARE;
    IF estado_atual IS DISTINCT FROM 'publicado' THEN
        RAISE EXCEPTION 'Uma tentativa precisa de uma versão publicada e imutável';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER tentativa_quiz_publicado BEFORE INSERT OR UPDATE ON tentativa
FOR EACH ROW EXECUTE FUNCTION exigir_quiz_publicado();

COMMENT ON VIEW tentativa_resultado IS 'Pontuação derivada do gabarito imutável, sem confiar em valor enviado pelo cliente.';
COMMENT ON TABLE resposta IS 'Somente o backend insere respostas; serializar por tentativa e validar a ordem antes do INSERT.';
COMMENT ON TABLE professor IS 'Autenticação futura do portal; as tabelas com gabarito não devem ser acessíveis ao cliente Unity.';
COMMENT ON TABLE participacao IS 'Token de alta entropia deve ser transmitido por HTTPS e armazenado somente como hash se persistido.';

INSERT INTO disciplina (id, nome, descricao, cor) VALUES
('matematica', 'Matemática', 'Números, contagem e formas', '#FFB84D'),
('portugues', 'Português', 'Letras, palavras e leitura', '#BAA4FF'),
('ciencias', 'Ciências', 'Natureza, sentidos e cuidado', '#58CFA8');

-- O backend futuro deve ainda aplicar autorização de professor por turma,
-- expiração de sessão, publicação apenas de quizzes autorizados, transações com
-- bloqueio da tentativa, validação de conclusão e política de retenção/exclusão.
-- A conta SQL do jogo não deve ter acesso direto de cliente nem privilégios DDL.
COMMIT;
