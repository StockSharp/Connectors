# Conector AscendEX

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector AscendEX** integra o StockSharp à API AscendEX Pro publicada. Um único adaptador cobre mercados spot cash, margem e futuros perpétuos, sendo útil para estratégias cripto multmercado e para preservar a implementação do protocolo documentado da plataforma.

## Principais recursos

- Descoberta de instrumentos spot, de margem e futuros perpétuos com estado de negociação, passos de preço e volume e limites de ordem.
- Cotações de nível 1, livros de nível 2, negócios públicos e candles OHLCV.
- Snapshots e histórico via REST e WebSockets separados em tempo real para spot e futuros.
- Saldos cash e margin, garantias e posições de futuros, ordens abertas e históricas e execuções.
- Ordens market, limit, stop-market e stop-limit com GTC, IOC, FOK, post-only e reduce-only para futuros.
- Cancelamento individual e em massa de ordens.
- Endereços configuráveis de REST e dos dois WebSockets, grupo de conta e modo cash ou margin.

Os dados públicos não exigem credenciais. As operações de portfólio e negociação exigem chave de API, segredo e o grupo de conta atribuído pela AscendEX.
