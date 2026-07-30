# Conector WazirX
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector WazirX** conecta o StockSharp à exchange centralizada de criptomoedas à vista WazirX. Ele traduz dados REST e WebSocket e operações da conta para o modelo unificado de mensagens do StockSharp.

## Principais recursos

- Descoberta de mercados à vista com passos de preço e quantidade e regras de negociação.
- Nível 1, negócios tick a tick, livros e candles em tempo real por streams públicos.
- Snapshots REST e histórico disponível de negócios e candles antes da continuação ao vivo.
- Ordens limitadas e stop-limit suportadas, cancelamento individual ou em grupo filtrado e atualizações de ordens e execuções.
- Saldos e carteiras por streams privados com conciliação REST.
- Operações privadas exigem chave e segredo API; dados públicos não precisam de credenciais de negociação.
- O adaptador não oferece ordens a mercado nem substituição atômica.
- Autenticação, símbolos, transportes, filtros e formatos WazirX ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais à vista WazirX, estratégias ao vivo, gráficos, monitoramento de contas e gestão de ordens.

Mercados, histórico, stop-limit, permissões, filtros, limites e disponibilidade dependem da WazirX e da conta conectada.
