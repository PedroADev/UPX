# Controles no macOS

O jogo usa o Input System da Unity. Conecte um controle Xbox ou Nintendo Switch Pro compatível ao macOS antes de jogar; também é possível conectar ou desconectar durante a partida. O jogo mantém teclado e mouse disponíveis.

| Ação | Controle |
| --- | --- |
| Caminhar | Analógico esquerdo ou direcional |
| Escolher um item nos menus | Direcional ou analógico esquerdo |
| Confirmar / interagir com portal ou livro | A |
| Voltar / cancelar | B |
| Acenar | Botão superior (Y no Xbox, X no Switch) |
| Ajuda / pausa | Menu no Xbox, + no Switch |
| Navegar pelos botões do HUD | View no Xbox, − no Switch |

Os rótulos acompanham o layout informado pelo controle. A disposição física de A e B difere entre Xbox e Nintendo. Enquanto um menu ou o teclado na tela está aberto, o direcional navega pela interface e o personagem fica parado.

## Digitar sem teclado físico

Selecione o campo de nome, código ou servidor e pressione A. O teclado na tela oferece letras, acentos, espaço, apagar e concluir. O campo de servidor também oferece números e símbolos de endereço. B cancela e mantém o texto anterior; X no Xbox ou Y no Switch apaga o último caractere. O nome aceita de 2 a 20 caracteres, como no modo de teclado físico.

Os menus de turma ficam fechados inicialmente. Selecione “Entrar na turma” para expandir; o foco rola automaticamente até o campo ou botão selecionado. Para escolher outro animal, confirme o personagem desejado antes de iniciar.

## Compatibilidade e verificação

O suporte usa os layouts de gamepad da Unity, incluindo Switch Pro HID. Um par de Joy-Con não é tratado como um controle combinado pelo jogo; depende do suporte do sistema/driver para expor um gamepad compatível. Não há driver próprio no projeto.

A validação automatizada usa estados reais do Input System com dispositivos virtuais; o teste final de Bluetooth/USB deve ser feito com o controle físico de destino.
