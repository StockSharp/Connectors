# Conector Pendle
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Pendle** conecta o StockSharp a um protocolo on-chain de negociação de rendimentos. Ele traduz dados do protocolo e operações da carteira para o modelo unificado de mensagens do StockSharp, permitindo usar assinaturas e fluxos de transação padrão nos mercados Pendle.

## Principais recursos

- Cobertura típica: ativos on-chain com rendimento, tokens de principal, tokens de rendimento e mercados Pendle.
- Pesquisa de instrumentos e dados de referência do protocolo.
- Dados de mercado suportados pelo adaptador: cotações de nível 1 e candles.
- Solicitações de candles históricos e atualizações contínuas de mercado para gráficos, análises e fluxos de estratégia.
- Conversão de tokens e envio de transações de blockchain suportados pelo provedor, incluindo as aprovações de tokens necessárias.
- Atualizações de carteira, saldos, posições e estado das execuções da wallet.
- Transporte HTTP e RPC, transações de wallet e formatos específicos do protocolo ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para monitorar mercados de rendimento, executar estratégias ao vivo, criar ferramentas cientes da carteira e obter cotações ou executar conversões por meio do Pendle.

Redes, mercados, tokens, cotações, funções de transação, taxas e disponibilidade dependem de Pendle, dos endpoints de API e RPC configurados, das condições atuais da blockchain e das permissões da carteira.
