# Conector m.Stock
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector m.Stock** conecta o StockSharp a uma corretora indiana e aos segmentos de bolsa por ela suportados. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

## Principais recursos

- Cobertura típica: ações, índices, futuros, opções, derivativos cambiais, fundos e títulos indianos.
- Pesquisa de instrumentos e dados de referência do provedor.
- Dados de mercado suportados pelo adaptador: cotações de nível 1, negócios tick a tick, livros de ordens e candles.
- Solicitações de candles históricos para gráficos, análises e backtests.
- Fluxos suportados pelo provedor para envio, alteração, cancelamento e execução de ordens.
- Atualizações de carteiras, saldos, posições, ordens e negócios.
- Assinaturas em tempo real pelo transporte de streaming do provedor.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em estratégias ao vivo, terminais, serviços de gestão de ordens e ferramentas de monitoramento que precisem de acesso direto a uma conta m.Stock.

Instrumentos, segmentos de bolsa, profundidade de dados, permissões de negociação, limites e disponibilidade dependem de m.Stock, das bolsas e da conta conectada.
