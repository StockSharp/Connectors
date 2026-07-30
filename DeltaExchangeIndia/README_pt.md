# Conector Delta Exchange India
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Delta Exchange India** conecta o StockSharp a uma plataforma indiana centralizada de derivativos de ativos digitais. Ele converte dados de mercado de futuros e opções, ordens e estado da conta para o modelo de mensagens padrão do StockSharp.

## Principais recursos

- Descoberta e dados de referência dos futuros e opções listados na Delta Exchange India.
- Instantâneos de nível 1 por REST e atualizações em tempo real por WebSocket; eventos históricos de nível 1 não estão disponíveis.
- Histórico recente de negócios por REST, limitado a 50 negócios por solicitação, além de negócios ao vivo por WebSocket.
- Instantâneos e atualizações ao vivo do livro com até 15 níveis; livros incrementais e históricos não são compatíveis.
- Candles históricos, até 1.999 barras por solicitação, e atualizações ao vivo nos intervalos aceitos pelo provedor.
- Ordens limitadas, a mercado e stop condicionais, com post-only, reduce-only, alteração, cancelamento e cancelamento em lote.
- Atualizações de carteira, saldos, posições, ordens e execuções por REST autenticado e canais privados.

## Uso típico

Use este conector em estratégias de derivativos ao vivo, terminais de negociação, serviços de gerenciamento de ordens e análises que precisem de negócios recentes ou histórico de candles da Delta Exchange India.

As operações privadas exigem credenciais de API e as permissões de conta necessárias. Instrumentos, alcance histórico, limites de solicitações e disponibilidade regional são controlados pelo provedor; ordens iceberg, vencimento absoluto e fechamento de posições em lote não estão implementados.
