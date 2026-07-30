# Conector DexScreener
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector DexScreener** leva ao StockSharp análises de pares de exchanges descentralizadas em várias redes por meio da API REST pública do DexScreener. É um adaptador de dados de mercado somente para leitura e não exige credenciais de API.

## Principais recursos

- Descoberta de pares por identificador da rede, endereço do token, endereço exato do par ou pesquisa de texto, com limites de salto e quantidade do StockSharp.
- Instantâneos de nível 1 com os últimos preços em USD e no token nativo, volume e variação de preço em 24 horas, liquidez e estado de negociação.
- Atualização periódica por REST das assinaturas ativas de nível 1; o intervalo é configurável e o padrão é 30 segundos.
- Cobertura das redes e dos pools de liquidez indexados pelo DexScreener.
- Acesso público sem chave de API ou sessão privada de conta.
- Sem eventos históricos de nível 1 ou transporte de streaming em tempo real.
- Sem negócios por tick, livros de ofertas, candles, envio de ordens, dados de carteira ou operações de conta.

## Uso típico

Use este conector para descobrir pares DEX, criar listas de observação, filtrar liquidez e alimentar painéis que precisem de métricas de mercado agregadas com atualização periódica.

Ele não é um conector de execução nem uma fonte de histórico de eventos adequada para backtesting. A cobertura de pares, os campos disponíveis, a atualidade dos dados e os limites de solicitações são determinados pelo DexScreener e pelos mercados descentralizados subjacentes.
