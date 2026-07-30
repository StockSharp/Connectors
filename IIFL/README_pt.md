# Conector IIFL
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector IIFL** conecta o StockSharp à IIFL Markets Open API para dados dos mercados indianos e operações de corretagem. Ele converte os serviços REST e MQTT da IIFL para o modelo de mensagens padrão do StockSharp.

## Principais recursos

- Descoberta de instrumentos na NSE, BSE e em segmentos de derivativos de ações, moedas e commodities, incluindo ações, índices, futuros e opções.
- Instantâneos de nível 1, livros de cinco níveis e atualizações de negócios por tick via REST e pelo fluxo MQTT oficial.
- Candles históricos e atualizações por sondagem de 1, 5, 10, 15 e 30 minutos; 1 hora; 1 dia; 1 semana; e 1 mês.
- Ordens a mercado, limitadas, stop limitadas e stop a mercado, com alteração e cancelamento individual; o cancelamento em lote não é compatível.
- Produtos e complexidades de ordem específicos da IIFL, preços de disparo, volume divulgado, proteção de mercado, identificadores de algoritmos e etiquetas do cliente.
- Recursos, ativos e posições da carteira, estado das ordens e execuções por instantâneos REST e atualizações MQTT privadas.
- Streaming MQTT configurável e sondagem REST do estado privado e dos candles ativos.

## Uso típico

Use este conector em terminais do mercado indiano, estratégias ao vivo, serviços de gerenciamento de ordens, monitoramento de carteiras e análises baseadas em candles.

A conexão exige credenciais de aplicativo da IIFL, identificador do cliente e o fluxo diário de autorização ou um token de sessão existente. Instrumentos, permissões de dados, recursos de ordens, horários e limites de solicitações dependem da conta IIFL e do segmento de bolsa.
