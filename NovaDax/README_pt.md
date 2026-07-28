# Conector NovaDAX

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector NovaDAX** integra o StockSharp ao mercado spot de criptomoedas da NovaDAX. O foco da corretora em pares com o real brasileiro torna o conector útil para monitoramento, coleta de dados e negociação automatizada no mercado cripto do Brasil.

## Principais recursos

- Descoberta de instrumentos spot com estado de negociação, precisão de preço e quantidade e limites mínimos de ordem.
- Cotações Level 1, livros Level 2, negociações públicas e histórico de candles OHLCV.
- Ticker, profundidade e negociações em tempo real via Socket.IO.
- Snapshots de mercado, negociações recentes e candles históricos via REST.
- Saldos, ordens ativas e históricas, estado das ordens e execuções privadas.
- Ordens a mercado, limite, stop-market e stop-limit com cancelamento individual e por instrumento.
- Endereços REST e Socket.IO, identificador de subconta e versão do Engine.IO configuráveis.

Os dados públicos estão disponíveis sem credenciais. As funções de carteira e negociação exigem chave de API e segredo da NovaDAX; uma subconta pode ser informada quando necessário.
