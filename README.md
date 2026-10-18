# Ilha do Saber

Jogo educativo 3D em português para crianças de aproximadamente 7 anos, em **Unity 6000.0.83f1**. A criança escolhe um apelido e um animal, explora portais e resolve desafios com explicações. A turma compartilha a mesma praça e as mesmas ilhas por um servidor de sessões em rede local.

## Jogar na Unity

1. Instale o Unity Editor **6000.0.83f1** pelo Unity Hub e abra esta pasta como projeto. Aguarde a importação dos pacotes e modelos.
2. Use **Ilha do Saber > Abrir jogo**. A preparação configura o renderizador 3D, a interface e os seis personagens Kenney automaticamente. A cena é `Assets/Aprender/Scenes/IlhaDoSaber.unity`.
3. Pressione **Play**, escolha um nome e um personagem e toque em **Jogar sozinho**. Esse modo funciona inteiramente sem servidor.

O projeto já aponta a cena do jogo como cena de build. Depois da preparação, Play inicia essa cena mesmo que outra esteja aberta. Para refazer a configuração, use **Ilha do Saber > Preparar projeto**. O código, as cenas, os prefabs, os tutoriais e os pacotes exclusivos do template multiplayer original foram removidos. O tema da interface foi mantido em `Assets/Aprender/Resources/UnityDefaultRuntimeTheme.tss`; as configurações ativas de renderização e entrada também foram preservadas.

## Controles Xbox e Nintendo no macOS

A branch `macOs` inclui caminhada, interação e navegação da interface por gamepad, com teclado na tela para inserir nome, código da turma e servidor. Consulte [os controles](docs/CONTROLES.md). Os títulos dos portais usam placas de alto contraste; a interface prioriza ações diretas e mantém a praça livre de painéis grandes.

## Versão para iPhone

A branch `iOS` inclui interface de toque em paisagem, joystick, teclado nativo e respeito à área segura da tela, com o **iPhone XR** como referência. O menu **Ilha do Saber > iOS > Preparar iPhone** configura o projeto; **Exportar projeto Xcode** verifica o módulo instalado antes de exportar. Veja [o guia de iPhone](docs/IOS.md) para instalar o módulo iOS da Unity, configurar o Xcode e conectar o aparelho à turma.

## Jogar com a turma

Requer Node.js **20 ou superior** em um computador da escola. Não há dependências npm para instalar.

```powershell
node backend/server.mjs
```

Também é possível iniciar pelo menu **Ilha do Saber > Servidor > Iniciar servidor local** no Editor. Esse processo é encerrado quando o Editor fecha ou recompila scripts. Para uma aula inteira, prefira o comando acima em um terminal separado.

1. No jogo, abra **Entrar na turma > Configuração da turma**. No computador do servidor, use `http://localhost:8787`. Nos demais, use `http://IP-DO-SERVIDOR:8787`.
2. Escolha nome e personagem e toque em **Criar uma sessão de turma**. O código gerado aparece no topo do jogo.
3. Os colegas configuram o mesmo endereço, digitam esse código e usam **Entrar**.
4. Colegas na mesma ilha aparecem como personagens e na lista lateral. Em ilhas diferentes, deixam de aparecer naquela sala. Não há colegas simulados.

Todos os computadores precisam alcançar a porta TCP 8787 do servidor na mesma rede; o responsável pela rede pode precisar liberar essa porta no firewall. O código identifica a sessão, enquanto o endereço identifica o servidor. Sessões e tokens ficam na memória: reiniciar o servidor exige criar uma nova turma. Presenças sem atualização desaparecem após 15 segundos.

O backend atual atende o protótipo em rede local. Para disponibilizar na internet, hospede o serviço com HTTPS e integre a identificação do professor e a autorização das turmas. A configuração permite HTTP para o teste na escola. Não é necessário configurar Unity Gaming Services, Relay ou uma conta cloud para esta versão.

## Controles e atividades

| Ação | Controle |
| --- | --- |
| Caminhar | W A S D, setas ou botões de direção na tela |
| Entrar em um portal | Aproximar-se e pressionar E; caminhar até o centro também entra |
| Abrir o livro de desafios | Aproximar-se e pressionar E ou usar o botão na tela |
| Navegar diretamente | Botões de disciplinas na praça e Voltar à praça |
| Acenar | Espaço ou Acenar |
| Ajuda / fechar painel | ? / Esc |

São seis personagens: cachorrinho, gatinho, coelhinho, leãozinho, pintinho e porquinho. Há três disciplinas, dois quizzes por disciplina e quatro questões por quiz: **24 questões** de matemática, português e ciências. Não há cronômetro, chat ou ranking entre crianças. Cada resposta mostra uma explicação e a alternativa correta. Concluir um quiz rende pelo menos uma estrela; 60% de acertos rende duas; 100%, três. Repetir preserva o melhor resultado.

O nome/avatar e o progresso ficam no dispositivo. As estrelas são deste perfil local do dispositivo, não um histórico individual autenticado de cada aluno. As tentativas online são corrigidas e registradas pelo servidor. Respostas duplicadas não pontuam duas vezes. A prática usa o gabarito incluído no projeto; o catálogo público online não envia gabaritos.

## Organização e integração futura

| Caminho | Responsabilidade |
| --- | --- |
| `Assets/Aprender/Runtime/GameBootstrap.cs` | Entrada, seleção de personagem, HUD, amigos e fluxo dos quizzes |
| `Assets/Aprender/Runtime/Core` | Contratos, comunicação HTTP, sessão, prática e persistência local |
| `Assets/Aprender/Runtime/World` | Personagens, movimento, câmera, portais, salas e interpolação remota |
| `Assets/Aprender/Editor` | Preparação automática de recursos, personagens e executável |
| `Assets/Aprender/Resources/quiz-catalog.json` | Conteúdo de demonstração, fonte do catálogo do servidor e da prática |
| `backend` | API HTTP de sessões, presença e correção das respostas |
| `docs/api.md` | Contrato e exemplos para a futura plataforma dos professores |
| `docs/database.sql` | Proposta PostgreSQL a partir do desenho fornecido |

A comunicação fica atrás de `GameSession`. A plataforma web poderá publicar o mesmo contrato de catálogo e substituir a fonte dos quizzes sem alterar o mundo 3D. A interface de autoria dos professores está fora desta etapa. O banco SQL é uma proposta para essa integração: **não é executado pelo backend atual**, que persiste as tentativas concluídas em JSON. Consulte o contrato para os detalhes de sessões, retenção e validação.

O progresso local usa `Application.persistentDataPath/aprender-progress-v1.json` com cópia de segurança. O histórico online padrão está em `backend/data/attempts.json`, ignorado pelo Git. Não há senha de criança, email ou chat no fluxo de entrada. Para a futura conta do professor, o esquema prevê hash de senha, não senha em texto puro.

Os modelos, UVs e texturas originais do pacote **Kenney Cube Pets** são usados nos prefabs. Os clips disponíveis são aproveitados; personagens sem clip usam movimento procedural suave. A licença original CC0 está em `Assets/kenney_cube-pets_1.0/License.txt`.

## Jardim verde e pacote de cenário

A praça tem gramado verde, caminhos de areia, céu azul e decoração do [Toon Environments — World Creator Pack Lite](https://assetstore.unity.com/packages/3d/environments/landscapes/toon-environments-world-creator-pack-lite-264325), importado em `Assets/Selcuk Gerceker/Toon Environments - World Creator Pack Lite`. A composição usa os prefabs originais `Tree`, `Shrub`, `Rock4`, `Grass` e `Flower`, com a paleta verde `Spring`. Os modelos, UVs, texturas e materiais originais são preservados; os materiais usados pela composição recebem cópias compatíveis com URP.

O jardim está em `Assets/Aprender/Resources/Environment/GreenPlaza.prefab` e é carregado tanto no Editor quanto no executável, na praça e nas salas de disciplinas. Para regenerá-lo após alterar a composição, use **Ilha do Saber > Cenário > Recriar jardim verde**. `Assets/Aprender/Generated/Environment/SourceAssets.txt` registra os prefabs e suas dependências reais para conferir a origem da decoração. Árvores e arbustos ficam nas bordas, deixando os caminhos e as aproximações dos portais livres.

## Capturas do jogo

Com o Editor fora do Play, use **Ilha do Saber > Capturas > Capturar demonstração completa**. O roteiro executa o jogo de verdade com o perfil temporário `Lia`, move o personagem e grava sete PNGs: entrada e personagens, caminhada, praça verde com interface, ilha de Matemática, lista de desafios, primeira questão e resposta com explicação. As imagens vêm da renderização da Game View, incluindo a UI, com resolução fixa de **1280×800**. A seleção anterior da Game View e o progresso local são restaurados ao terminar; a primeira resposta usa a correção real do modo prática.

Cada execução cria uma pasta `docs/screenshots/demo-...` com as imagens e um `manifest.json`: dimensões verificadas no próprio PNG, posição do personagem, entrada de movimento, frames renderizados, resultado da resposta, erros e avisos. O roteiro também pode ser iniciado explicitamente por `Aprender.Editor.ShowcaseCapture.Begin()` ou pela presença de um arquivo vazio `Temp/ilha-capture.request`; nenhum conteúdo desse arquivo é interpretado.

Para capturas avulsas durante o Play, use **Ilha do Saber > Capturas > Capturar Game View**. Essa opção grava em `docs/screenshots` com resolução 2× a partir do tamanho atual da Game View. O Console informa o caminho solicitado.

## Verificação

```powershell
# API real com servidores HTTP temporários e arquivos isolados
node --test backend/test/*.test.mjs

# Lógica C# com APIs Unity e transporte simulados + sintaxe (PowerShell 7)
pwsh -NoProfile -File tools/verify-client.ps1
```

Foram aprovados **13 testes HTTP** e **37 verificações da lógica do cliente**, incluindo prática, persistência, troca de sessão e retry com transporte simulado. Os scripts C# passaram na análise sintática Roslyn. Os stubs testam regras e persistência e não substituem as bibliotecas ou o ciclo de execução da Unity.

**Validação na Unity 6000.0.83f1:** os scripts Runtime e Editor compilaram com as bibliotecas reais da Unity, sem erros ou avisos. O parser real `ExCSS.Unity.dll` reproduziu os quatro erros USS originais com cores hexadecimais de oito dígitos; após a troca por `rgba(...)`, o USS atual passou com 156 regras e zero erros. O roteiro também executou o jogo em Play Mode, conferiu deslocamento real do personagem, resposta e explicação de quiz, e gerou capturas em 1280×800 com o asset importado. A galeria está em [docs/screenshots/README.md](docs/screenshots/README.md).

Além da validação automática e visual, antes de usar com uma turma confira os dispositivos e a rede de destino:

- Abra pelo menu, aguarde a importação e confirme que o Console não mostra erros de compilação ou de USS.
- Execute um quiz completo no modo prática, repetindo com respostas diferentes e reabrindo o jogo para conferir o progresso.
- Abra duas instâncias (Editor + executável), entre na mesma turma e confira deslocamento, troca de ilha e saída de um colega.
- Pare o servidor durante um quiz e confira a mensagem de reconexão e o retorno à entrada.
- Confira a interface em 1280×800 e 1920×1080, além da tela e dos controles por toque do dispositivo de destino.

Para gerar o executável, instale **Windows Build Support** no Hub e use **Ilha do Saber > Gerar executável Windows**. O resultado ficará em `Builds/IlhaDoSaber/IlhaDoSaber.exe`.

Referências técnicas: [UI Toolkit / PanelSettings na Unity 6](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.PanelSettings.html) e [URP oficial da Unity 6](https://github.com/Unity-Technologies/Graphics/tree/6000.0/staging/Packages/com.unity.render-pipelines.universal).
