# Conector Tokocrypto

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Tokocrypto** integra o StockSharp ao mercado à vista MAIN da Tokocrypto. Ele é indicado para negociação de criptomoedas voltada ao mercado indonésio e para aplicações que precisam dos dados da Tokocrypto no modelo de mensagens do StockSharp.

## Principais recursos

- Descoberta de instrumentos spot MAIN com filtros de preço, volume e ordem mínima.
- Cotações de nível 1, livros de nível 2, negócios públicos e candles OHLCV.
- Tickers, livros parciais, negócios e candles em tempo real por WebSocket.
- Candles históricos e snapshots recentes pela API REST pública.
- Saldos spot, ordens abertas e históricas e histórico de execuções privadas.
- Ordens a mercado, limite, stop-market, stop-limit, post-only, IOC e FOK.
- Cancelamento individual e em grupo; endereços REST de conta, REST de mercado e WebSocket configuráveis.

## Uso típico

Use o conector em robôs de negociação, terminais, coletores de dados, serviços de monitoramento e sistemas de gestão de ordens para instrumentos spot da Tokocrypto.

Os dados públicos não exigem credenciais. Operações de conta e negociação exigem uma chave de API e um segredo da Tokocrypto.
