# Histórico de versões

## 1.1.0 — demonstração local

- Adicionado o portal Farol da Turma para criar turmas, quizzes e sessões.
- Backend passou a usar SQLite local como fonte compartilhada de dados.
- O jogo consulta o catálogo específico da turma depois de entrar na sessão.
- Resultados de quizzes concluídos ficam disponíveis no painel do professor.
- Estrelas passam a ser calculadas pelo servidor e preservadas no SQLite, inclusive em bancos existentes.
- Adicionados controles de desktop e preparação de exportação iOS; a exportação final ainda requer Mac, Xcode e aparelho.
- Corrigido o teste ponta a ponta, que agora usa somente o contrato público real.
- Atualizada a documentação de inicialização local, rede e testes.

## 1.0.0 — protótipo do jogo

- Jogo Unity com personagens, portais, quizzes e prática individual.
- Servidor local de sessões e presença com catálogo de demonstração.
