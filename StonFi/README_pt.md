# Conector STON.fi
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector STON.fi** conecta o StockSharp aos pools de liquidez da STON.fi e à blockchain TON. Pools configurados ou descobertos viram instrumentos, e cotações de swaps, eventos, saldos e swaps enviados são convertidos em mensagens do StockSharp.

## Principais recursos

- Descoberta de pools configurados ou de um conjunto limitado de pools populares da STON.fi, com metadados dos tokens.
- Preços de compra e venda de nível 1 calculados por simulações executáveis e atualizados por consulta.
- Negócios tick a tick históricos e ao vivo a partir de eventos dos pools TON, com candles construídos desses swaps.
- Swaps imediatos a mercado com mnemônico TON Wallet V4, slippage configurável e transmissão pelo TON Center.
- Saldos de tokens e acompanhamento do estado da ordem e execução do swap.
- O histórico é limitado pelo intervalo de blocos TON configurado; dados ao vivo dependem de consultas.
- Não há livro centralizado, ordens limitadas pendentes, substituição nem cancelamento.
- Dados REST, unidades TON, assinatura e eventos ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para monitorar cotações de DEX na TON, analisar pools, criar estratégias de swap, acompanhar carteiras e executar diretamente na STON.fi.

Cobertura, cotações, histórico, rotas, taxas, finalidade e disponibilidade dependem da STON.fi, TON Center, endpoints e blockchain.
