# Conector Zonda Crypto
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Zonda Crypto** conecta o StockSharp à exchange centralizada de criptomoedas à vista zondacrypto. Ele traduz dados REST e WebSocket e operações da conta para o modelo unificado de mensagens do StockSharp.

## Principais recursos

- Descoberta de mercados à vista com moeda, passos de preço e quantidade e valor mínimo.
- Nível 1, negócios tick a tick e snapshots e atualizações do livro em tempo real por streams públicos.
- Snapshots REST e histórico recente disponível antes da continuação ao vivo; não oferece candles.
- Ordens a mercado e limitadas com opções GTC, IOC, FOK e post-only suportadas.
- Cancelamento individual ou em grupo filtrado e atualizações de ordens e execuções; não há substituição atômica.
- Saldos de carteira por streams privados e conciliação REST periódica.
- Operações privadas exigem chave e segredo API; dados públicos não precisam de credenciais de negociação.
- Autenticação, códigos, transportes, filtros e formatos zondacrypto ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais à vista zondacrypto, estratégias ao vivo, análise de negócios recentes, monitoramento de contas e gestão de ordens.

Mercados, histórico recente, permissões, opções de ordem, limites e disponibilidade dependem da zondacrypto e da conta conectada.
