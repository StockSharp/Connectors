# Conector DeepBook
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector DeepBook** integra o StockSharp ao protocolo de liquidez DeepBook na Sui. Ele combina o indexador público DeepBook com um nó completo Sui por gRPC para dados dos pools, saldos da carteira e swaps imediatos assinados localmente.

## Principais recursos

- Descoberta de pools DeepBook com filtro opcional por nome, identificador ou código do instrumento.
- Consulta de snapshots de Level 1, profundidade e negócios tick históricos ou atualizados por sondagem.
- Download e atualização por consulta de candles entre 1 minuto e 7 dias.
- Configuração de indexador, nó Sui, pacote, objeto de relógio, profundidade, histórico e intervalo de consulta.
- Exposição dos saldos de tokens Sui como portfólio StockSharp quando há endereço de carteira.
- Envio de ordem a mercado como swap DeepBook assinado localmente e protegido contra slippage.
- Acompanhamento do digest da transação Sui e da execução do swap.

## Uso típico

Use este conector para acompanhar pools DeepBook, coletar dados da DEX Sui ou executar swaps imediatos a partir de uma carteira configurada. Dados públicos não exigem chave privada; o portfólio requer endereço de carteira e o swap exige sua chave de assinatura Ed25519.

A interface transacional representa swaps imediatos, não ordens DeepBook mantidas no livro. Ordens limitadas, condicionais, post-only e parâmetros de validade não estão disponíveis; uma transação Sui executada não pode ser cancelada, substituída nem cancelada em grupo. Latência, cobertura do indexador, slippage, gas, liquidez e finalidade da Sui afetam o resultado.
