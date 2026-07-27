# Conector Fireblocks
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Fireblocks** conecta o StockSharp a um serviço institucional, de custódia ou liquidação de ativos digitais. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

## Principais recursos

- Cobertura típica: ativos digitais, Forex e CFDs.
- Pesquisa de instrumentos e dados de referência do provedor.
- Solicitações de dados históricos para gráficos, análises e backtests.
- Fluxos de contas, transferências e transações suportados pelo provedor.
- Atualizações de carteiras, saldos, posições e estado das execuções.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em estratégias ao vivo, terminais, serviços de gestão de ordens e ferramentas de monitoramento que precisem de acesso direto ao provedor.

Instrumentos, profundidade de dados, permissões de negociação, limites e disponibilidade dependem de Fireblocks, do plano de API e da conta conectada.
