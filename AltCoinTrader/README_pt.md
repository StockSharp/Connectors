# Conector AltCoinTrader

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector AltCoinTrader** integra o StockSharp ao mercado spot sul-africano da AltCoinTrader. Os livros denominados em ZAR são úteis para descoberta local de preços, monitoramento, coleta de dados e negociação automatizada de criptoativos.

## Principais recursos

- Descoberta de instrumentos spot com estado de negociação, precisão de preço e quantidade e valor mínimo de ordem.
- Cotações Level 1, livros Level 2 e negociações públicas.
- Ticker, profundidade e negociações em tempo real pelo WebSocket público.
- Snapshots de mercado e negociações públicas recentes via REST.
- Saldos, ordens abertas e históricas, execuções privadas e atualizações da conta pelo WebSocket autenticado.
- Ordens limite com GTC, IOC e FOK, ordens a mercado e cancelamento individual ou em lote com filtros.
- Endereços REST e WebSocket configuráveis.

Os dados públicos estão disponíveis sem credenciais. As funções de carteira e negociação exigem chave de API e segredo da AltCoinTrader com as permissões adequadas.
