# Conector ThetaData
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector ThetaData** conecta o StockSharp a um serviço profissional de dados e análises de mercado. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

## Principais recursos

- Cobertura típica: ações, opções, índices.
- Pesquisa de instrumentos e dados de referência do provedor.
- Dados de mercado suportados pelo adaptador: cotações de nível 1, negócios tick a tick, livros de ordens e candles.
- Solicitações de dados históricos para gráficos, análises e backtests.
- Assinaturas em tempo real pelo transporte de streaming do provedor.
- Este adaptador é destinado a dados de mercado e não encaminha ordens.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para alimentar gráficos, armazenamento de mercado, análises, pesquisas e testes de estratégias com dados do provedor.

Instrumentos, profundidade de dados, permissões de negociação, limites e disponibilidade dependem de ThetaData, do plano de API e da conta conectada.
