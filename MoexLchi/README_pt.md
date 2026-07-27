# Conector MOEX LCHI
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector MOEX LCHI** conecta o StockSharp a uma fonte de dados de bolsa e de mercado da Rússia. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

## Principais recursos

- Dados de mercado suportados pelo adaptador: eventos do log de ordens.
- Solicitações de dados históricos para gráficos, análises e backtests.
- Este adaptador é destinado a dados de mercado e não encaminha ordens.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para alimentar gráficos, armazenamento de mercado, análises, pesquisas e testes de estratégias com dados do provedor.

Instrumentos, profundidade de dados, permissões de negociação, limites e disponibilidade dependem de MOEX LCHI, do plano de API e da conta conectada.
