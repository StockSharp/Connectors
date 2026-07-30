# Conector 0x
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector 0x** conecta o StockSharp à 0x Swap API v2 e a redes EVM suportadas. Pares de tokens configurados viram instrumentos, e preços executáveis, saldos e swaps roteados são convertidos em mensagens do StockSharp.

## Principais recursos

- Descoberta de pares configurados em Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche e Linea.
- Preços bid e ask de nível 1 obtidos por consulta de preços executáveis da 0x.
- Obtenção, assinatura e transmissão de swaps imediatos a mercado pelo JSON-RPC da rede escolhida.
- Aprovação automática opcional de allowance e configuração de slippage, volume de sondagem e tempo limite de recibo.
- Saldos de tokens e acompanhamento de recibos, estados de ordens e execuções.
- Chave da 0x Dashboard, carteira, pares e endpoints API e RPC configuráveis.
- Não oferece ticks, livros, candles, histórico, ordens pendentes, substituição nem cancelamento.
- Rotas, unidades, aprovações, assinatura e recibos EVM ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para monitorar preços executáveis de tokens, criar painéis de carteira e executar diretamente swaps roteados pela 0x em uma rede EVM suportada.

Cobertura, rotas, liquidez, impacto, gás, aprovações, finalidade e limites dependem da 0x, da rede e do provedor RPC.
