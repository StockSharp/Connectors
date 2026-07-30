# Conector XRPL
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector XRPL** conecta o StockSharp à exchange descentralizada integrada ao XRP Ledger. Pares configurados, livros do ledger, ofertas executadas, saldos e transações assinadas são convertidos em mensagens do StockSharp.

## Principais recursos

- Descoberta de pares configurados de XRP e tokens emitidos, com seleção opcional de domínio DEX permissionado.
- Nível 1 e livros com profundidade configurável e atualizações contínuas do ledger.
- Negócios tick a tick históricos e ao vivo derivados de mudanças no livro, com candles construídos da atividade do ledger.
- Ofertas limitadas e IOC a mercado com proteção de preço, além de cancelamento, substituição e cancelamento em grupo acompanhado.
- Saldos, ofertas abertas, estados, execuções, taxas e status de transações.
- Dados públicos exigem apenas RPC e WebSocket; negociação requer endereço clássico e family seed.
- O histórico é limitado pela varredura de ledgers e snapshots usam o intervalo de consulta configurado.
- Valores, emissores, assinatura, sequências, taxas e eventos XRPL ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais DEX XRPL, análise do ledger, estudos históricos, monitoramento de contas e execução direta de ofertas.

Pares, liquidez, histórico, custos, finalidade, domínios permissionados e endpoints dependem do estado da XRPL e do serviço configurado.
