# Conector Dexalot
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Dexalot** conecta o StockSharp ao livro central de ordens limitadas on-chain da Dexalot, na Dexalot L1 baseada em Avalanche. Ele combina dados públicos por REST e WebSocket com chamadas a contratos EVM para negociação à vista e estado da conta.

## Principais recursos

- Descoberta e dados de referência dos pares de tokens à vista da Dexalot.
- Instantâneos de nível 1 e do livro por leitura de contratos, seguidos de atualizações ao vivo por WebSocket; eventos históricos de nível 1 e do livro não estão disponíveis.
- Fluxos de negócios e candles por WebSocket, com filtros de data e quantidade sobre o histórico fornecido pelo provedor e entrega contínua ao vivo.
- Candles de 5, 15 e 30 minutos; 1 e 4 horas; e 1 dia.
- Ordens limitadas e a mercado on-chain, incluindo post-only e prevenção de autonegociação configurável.
- Alteração, cancelamento individual e cancelamento em lote; ordens iceberg, vencimento absoluto e fechamento de posições em lote não são compatíveis.
- Saldos de tokens da carteira, histórico de ordens e execuções e reconciliação do estado privado por REST, WebSocket e RPC de EVM.

## Uso típico

Use este conector em estratégias à vista ao vivo, terminais e serviços de gerenciamento de ordens que precisem do livro da Dexalot e de execução on-chain.

A negociação exige endereço de carteira e chave privada e gera custos de gas e latência de confirmação da rede. Pares disponíveis, histórico fornecido pelo fluxo, limites da API, disponibilidade dos contratos e finalidade dependem da Dexalot e dos endpoints de rede escolhidos.
