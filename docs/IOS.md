# Ilha do Saber no iPhone

A branch `iOS` parte da versão `macOs`: mantém os quizzes, o progresso, o servidor e os controles compartilhados. A interface do iPhone usa toque, analógico virtual, teclado nativo e margens para o recorte da tela e o indicador de início.

## Abrir e testar no Editor

1. Abra o projeto na Unity **6000.0.83f1**.
2. Abra a cena pelo menu **Ilha do Saber > Abrir jogo**.
3. Para uma prévia no próprio Editor, selecione o objeto `GameBootstrap` da cena e marque **Preview IPhone Layout** no Inspector. Desmarque a opção quando voltar ao teste desktop. A opção muda apenas a interface, não a plataforma de build.
4. Para o **iPhone XR**, use uma Game View de **896×414** em paisagem e mantenha **Preview Safe Area Insets** em `(44, 0, 44, 21)`. Esses valores simulam o recorte e o indicador de início nessa resolução de prévia. Para uma prévia de **1792×828 pixels**, dobre as margens para `(88, 0, 88, 42)`. O aparelho usa sua área segura real nos dois sentidos da paisagem. Se o Device Simulator estiver disponível, confira também a simulação XR.
5. Inicie uma prática, movimente o analógico, abra um portal e conclua um quiz. Confira que o movimento para ao abrir uma janela, trocar de tela ou perder o foco do app.

O Editor permite conferir disposição e fluxo. O teclado do iOS, a permissão de rede local, a assinatura, o desempenho e o toque com vários dedos precisam de validação no iPhone. O Device Simulator não emula desempenho nem o backend gráfico do aparelho.

O aparelho de referência é o **iPhone XR com iOS 18**. O projeto aceita iOS 15 ou superior; a instalação no XR ainda precisa ser conferida no aparelho. A escala do Editor é um pouco mais conservadora que a escala calculada pela densidade de pixels do XR.

Para ver as opções de turma, deslize o formulário para cima: **Entrar na turma** fica abaixo de **Jogar sozinho**. Perguntas e explicações maiores também aceitam rolagem por gesto. O joystick continua disponível se um controle estiver conectado.

## Instalar os componentes de desenvolvimento

A instalação auditada desta máquina possui suporte de build para macOS. Ainda faltam **iOS Build Support** para esta mesma versão da Unity e **Xcode**. O código não instala esses componentes automaticamente.

1. No Unity Hub, abra **Installs**, clique na engrenagem da Unity 6000.0.83f1 e use **Add modules > iOS Build Support**.
2. Instale o Xcode compatível com o macOS, abra-o e conclua a instalação dos componentes adicionais. Se o Xcode solicitar, selecione as Command Line Tools nas configurações.
3. Conecte o iPhone ao Mac, desbloqueie-o e confirme a confiança no computador. Ative **Ajustes > Privacidade e Segurança > Modo de Desenvolvedor** se o sistema solicitar.
4. Adicione seu Apple ID no Xcode. Um Apple ID gratuito pode assinar testes pessoais; a distribuição pela App Store exige participação no Apple Developer Program.

## Preparar e exportar

1. Saia do modo Play.
2. Use **Ilha do Saber > iOS > Preparar iPhone**. A configuração usa iOS 15 ou superior, iPhone, IL2CPP, Metal e paisagem nos dois sentidos.
3. Use **Ilha do Saber > iOS > Exportar projeto Xcode**. A Unity gera o projeto em `Builds/iOS`, uma pasta ignorada pelo Git.
4. Abra `Builds/iOS/Unity-iPhone.xcodeproj` no Xcode.
5. No target **Unity-iPhone > Signing & Capabilities**, escolha sua equipe. Ative a assinatura automática se apropriado para sua conta e escolha um **Bundle Identifier** disponível para ela. O valor inicial do projeto é `com.ilhadosaber.app`; altere-o em **Player Settings > iOS** se necessário.
6. Selecione o iPhone conectado como destino e use **Run**. Resolva eventuais solicitações de confiança/assinatura apresentadas pelo aparelho.

A exportação cria um projeto Xcode. Ela não publica o jogo, não cria um IPA assinado e não instala o app por conta própria. Certificados, equipe de assinatura e conta Apple não estão armazenados no repositório.

## Jogar com uma turma pela rede local

A prática individual funciona sem servidor. Para uma turma:

1. No Mac, inicie **Ilha do Saber > Servidor > Iniciar servidor local**, ou execute `node backend/server.mjs` na pasta do projeto.
2. Mantenha o Mac e o iPhone na mesma rede Wi-Fi. O Mac precisa estar ligado e o servidor precisa aceitar conexões; redes de convidados ou escolares podem isolar os aparelhos.
3. No campo de servidor do iPhone, use o endereço local do **Mac**, por exemplo `http://meu-mac.local:8787` ou `http://192.168.1.20:8787`. O nome/endereço exato depende da sua rede e aparece nas configurações de rede/compartilhamento do macOS.
4. **Não use `localhost` ou `127.0.0.1` no iPhone:** esses endereços apontam para o próprio aparelho.
5. Na primeira conexão, permita o acesso à rede local. Se negar, revise a permissão em **Ajustes > Privacidade e Segurança > Rede Local** antes de tentar novamente.
6. Crie a turma no Mac e digite o código no iPhone; confirme que os dois participantes aparecem e que as respostas são registradas.

O pós-build adiciona a explicação de acesso à rede local no `Info.plist`. Permite HTTP apenas para nomes locais e faixas privadas IPv4 da rede escolar: `10.0.0.0/8`, `172.16.0.0/12` e `192.168.0.0/16`. Em iOS 17 ou superior, as exceções explícitas de IP/CIDR são necessárias para endereços numéricos. Para um servidor na Internet, use HTTPS com certificado válido. Não é feita descoberta automática por Bonjour.

## Conferência antes de distribuir

No aparelho, confira entrada de apelido com acentos, código e URL; teclado sem cobrir o campo em edição; leitura das perguntas; ida e volta entre ilhas; conclusão de quizzes e salvamento após fechar o app. Teste o analógico simultaneamente com o botão de ação, bloqueio/desbloqueio da tela, ida ao segundo plano e recuperação após negar a rede local. Meça desempenho e aquecimento no iPhone que será utilizado.

## Verificação realizada

Na Unity 6000.0.83f1, em uma cópia temporária do projeto, passaram compilação, escala da interface, área segura simulada, configuração dos teclados, rolagem dos campos, validação do servidor, eventos de toque do UI Toolkit, captura de múltiplos ponteiros, cancelamento de movimento e ciclo completo de prática com progresso salvo. A prévia XR foi conferida em **896×414**; uma tela compacta de **568×320** também foi exercitada, usando rolagem quando necessária.

As capturas vêm da renderização real da Unity com Metal e UI Toolkit, com o layout de iPhone ativado no Editor:

- [Entrada e personagens](screenshots/ios-preview/01-entrada-xr.png)
- [Praça e títulos dos portais](screenshots/ios-preview/02-praca-xr.png)
- [Quiz](screenshots/ios-preview/03-quiz-xr.png)
- [Pergunta longa](screenshots/ios-preview/04-pergunta-longa-xr.png)
- [Escopo e limites da validação](screenshots/ios-preview/validation.json)

O arquivo de permissões de rede gerado passou na análise de sintaxe do macOS. A exportação detecta e explica a ausência do módulo iOS. Não foi gerado um projeto Xcode ou IPA nesta máquina; assinatura, instalação e comportamento real no iPhone permanecem pendentes dos componentes e do aparelho.

## Referências

- [Unity: preparação do ambiente iOS](https://docs.unity3d.com/6000.0/Documentation/Manual/ios-environment-setup.html)
- [Apple: especificações do iPhone XR](https://support.apple.com/pt-br/111868)
- [Apple: modelos compatíveis com iOS 18](https://support.apple.com/en-asia/guide/iphone/-iphe3fa5df43/ios)
- [Unity: exportação e compilação do projeto Xcode](https://docs.unity3d.com/6000.0/Documentation/Manual/iphone-BuildProcess.html)
- [Unity: Device Simulator para iOS](https://docs.unity3d.com/6000.0/Documentation/Manual/ios-device-simulator.html)
- [Apple: descrição de acesso à rede local](https://developer.apple.com/documentation/bundleresources/information-property-list/nslocalnetworkusagedescription)
- [Apple: exceções ATS para IPs e redes CIDR](https://developer.apple.com/documentation/BundleResources/Information-Property-List/NSAppTransportSecurity/NSExceptionDomains)
