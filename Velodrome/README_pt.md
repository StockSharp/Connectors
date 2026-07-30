# Conector Velodrome
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Velodrome** conecta o StockSharp aos pools clássicos e Slipstream da Velodrome na Optimism. Ele converte pools, cotações executáveis, swaps on-chain, saldos e transações enviadas em mensagens do StockSharp.

## Principais recursos

- Descoberta de pools clássicos e de liquidez concentrada configurados, com metadados dos tokens.
- Preços bid e ask de nível 1 derivados de sondagens executáveis, com WebSocket e fallback por consulta.
- Negócios tick a tick históricos e ao vivo a partir de logs de swaps, com candles construídos desses eventos.
- Swaps imediatos a mercado assinados com chave EVM opcional, incluindo gestão de allowances e slippage.
- Saldos de tokens, recibos de transações e atualizações de ordens e execuções.
- A coleta histórica é limitada por intervalos e quantidades de blocos Optimism configurados.
- Não há livro centralizado, ordens limitadas pendentes, substituição atômica nem cancelamento.
- RPC, unidades, variantes de pool, assinatura e logs ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para monitoramento DEX na Optimism, análise de pools Velodrome, backtests por eventos, acompanhamento de carteiras e swaps diretos.

Cobertura, preços, liquidez, histórico RPC, gás, finalidade e disponibilidade dependem da Velodrome, Optimism e serviços RPC.
