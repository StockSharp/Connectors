# Conector Chainflip
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Chainflip** integra o StockSharp à rede de liquidez entre cadeias Chainflip. Ele combina dados públicos da State Chain e do serviço de swap com uma configuração opcional de carteira para enviar swaps entre cadeias pelo modelo transacional do StockSharp.

## Principais recursos

- Descoberta de pools e ativos aceitos pela Chainflip.
- Recebimento de Level 1, profundidade dos pools e negócios derivados do estado e das execuções.
- Configuração dos endpoints de State Chain, cotações, Ethereum e Arbitrum.
- Solicitação de cotação e envio de ordem a mercado como swap entre cadeias protegido.
- Acompanhamento dos swaps enviados e exposição dos saldos da carteira por mensagens de portfólio.
- Configuração de endereços de destino para ativos nas cadeias aceitas.

## Uso típico

Use este conector para monitorar a liquidez da Chainflip ou executar swaps imediatos entre cadeias a partir de uma carteira configurada. Dados públicos não exigem chave de assinatura; a execução requer endereço da carteira, chave privada, endereços de destino e endpoints operacionais.

Esta é uma integração de protocolo, não uma interface de ordens de bolsa centralizada. O adaptador não oferece candles, ordens limitadas, condicionais ou mantidas no livro. Depois de transmitida, a transação de swap não pode ser cancelada, substituída nem cancelada em grupo. Taxas, finalidade, liquidez, slippage e disponibilidade das cadeias afetam a execução.
