# Conector Coinstore

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Coinstore** liga o StockSharp ao mercado spot de criptomoedas da Coinstore. É útil para acompanhar o amplo mercado de listagens da bolsa e automatizar negociações em pares de criptoativos e stablecoins.

## Principais recursos

- Descoberta de instrumentos spot com estado, precisão de preço e quantidade e mínimos de ordem.
- Dados Level 1, livros Level 2, negócios públicos e candles OHLCV.
- Ticker, profundidade, negócios e candles em tempo real via WebSocket.
- Negócios recentes, snapshots do livro e histórico de candles via REST.
- Saldos, ordens ativas, estado de ordens e execuções privadas.
- Ordens a mercado, limite, post-only e IOC, com cancelamento individual e em lote.
- Endereços REST e WebSocket configuráveis.

Dados públicos não exigem credenciais. Recursos de carteira e negociação exigem chave de API e segredo da Coinstore. O estado privado é atualizado por solicitações REST autenticadas.
