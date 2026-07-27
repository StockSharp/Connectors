# Conector RavenPack
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector RavenPack** conecta o StockSharp a um serviço de notícias financeiras e dados de eventos. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

## Principais recursos

- Cobertura típica: ações, Forex e CFDs, commodities, índices.
- Pesquisa de instrumentos e dados de referência do provedor.
- Dados de mercado suportados pelo adaptador: notícias financeiras.
- Solicitações de dados históricos para gráficos, análises e backtests.
- Este adaptador é destinado a dados de mercado e não encaminha ordens.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para incorporar notícias e eventos do provedor a monitoramento, análises, alertas e estratégias orientadas a eventos.

Instrumentos, profundidade de dados, permissões de negociação, limites e disponibilidade dependem de RavenPack, do plano de API e da conta conectada.
