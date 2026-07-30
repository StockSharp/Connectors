# Conector MarketData.app
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector MarketData.app** conecta o StockSharp a um serviço profissional de dados de mercado. Ele traduz dados específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas solicitações e fluxos com diferentes fontes de dados.

## Principais recursos

- Cobertura típica: ações, ETFs, opções, índices e fundos.
- Pesquisa de instrumentos, incluindo cadeias de opções, e dados de referência do provedor.
- Dados de mercado suportados pelo adaptador: instantâneos de cotações de nível 1 e candles.
- Solicitações de candles históricos para gráficos, análises e backtests; o serviço não fornece candles de opções.
- Este adaptador é destinado a dados de mercado e não encaminha ordens.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para alimentar gráficos, descobrir instrumentos e opções, armazenar dados de mercado, realizar análises e pesquisas e testar estratégias com dados do provedor.

Instrumentos, profundidade do histórico, ajustes, limites, direitos de dados e disponibilidade dependem de MarketData.app e do plano de API conectado.
