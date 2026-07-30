# Conector KyberSwap
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector KyberSwap** conecta o StockSharp à KyberSwap Aggregator API v1 e a redes EVM. Ele expõe os pares de tokens configurados como instrumentos do StockSharp, deriva cotações executáveis das rotas do agregador e envia swaps on-chain assinados.

## Principais recursos

- Descoberta de pares de tokens configurados e seus metadados na Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche e Linea.
- Cotações de compra e venda de nível 1 calculadas a partir de rotas executáveis do agregador para um volume de sondagem configurável.
- Sondagem REST periódica das assinaturas ativas de nível 1; eventos históricos de cotações e transporte por streaming não estão disponíveis.
- Swaps a mercado imediatos, assinados localmente e transmitidos por JSON-RPC de EVM, com slippage configurável e aprovação automática de tokens.
- Saldos de tokens da carteira e atualizações de portfólio por chamadas à rede.
- Acompanhamento por hash dos swaps enviados pelo conector até que um recibo EVM confirme sucesso ou falha.
- Sem negócios por tick, livros de ofertas, candles, ordens limitadas ou alteração e cancelamento de transações já transmitidas.

## Uso típico

Use este conector para monitorar cotações DEX considerando as rotas e automatizar swaps a mercado nas redes EVM compatíveis.

As cotações podem ser consultadas sem credenciais de negociação, mas a execução exige carteira, chave privada e um endpoint RPC operacional. Definições de tokens, liquidez das rotas, aprovações, custos de gas, slippage, latência do recibo, limites da API e estado da rede afetam cada swap.
