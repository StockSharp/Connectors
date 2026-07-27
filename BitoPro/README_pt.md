# Conector BitoPro

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector BitoPro** liga o StockSharp à BitoPro, uma bolsa de criptomoedas regulamentada e voltada ao mercado de Taiwan, com mercados spot ativos em TWD.

## Principais recursos

- Descoberta de instrumentos spot, precisão de preço e quantidade e limites de negociação.
- Dados Level 1, snapshots do livro Level 2 e negócios públicos.
- Tickers, livros e negócios em tempo real por WebSocket.
- Candles OHLCV históricos em todos os intervalos oferecidos pela BitoPro.
- Saldos, ordens abertas e históricas e histórico de negócios privados.
- Ordens limit, market, stop-limit e post-only, com cancelamento individual e em grupo.
- Endereços REST e WebSocket configuráveis.

## Uso típico

Adequado para robôs, terminais, coletores de dados dos mercados TWD, monitoramento e gerenciamento de ordens.

Os dados públicos não exigem credenciais. Operações de conta e negociação exigem e-mail, chave API e segredo. A BitoPro recebe compras market na moeda de cotação; o conector converte o volume base do StockSharp usando o último preço público.
