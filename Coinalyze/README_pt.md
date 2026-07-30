# Conector Coinalyze
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Coinalyze** integra o StockSharp à API de análise do mercado de criptomoedas da Coinalyze. Ele mapeia preços históricos e indicadores de derivativos para candles padrão do StockSharp em instrumentos futuros ou spot.

## Principais recursos

- Seleção de instrumentos futuros ou spot e restrição opcional da descoberta por bolsa.
- Download de candles históricos de preço, interesse em aberto, taxa de financiamento, liquidações ou relação entre posições compradas e vendidas.
- Uso dos períodos aceitos pela API Coinalyze.
- Conversão opcional dos valores de interesse em aberto e liquidações para dólares.
- Configuração de limite de até 2.000 registros históricos por solicitação.
- Autenticação das solicitações com um token de API Coinalyze.

## Uso típico

Use este conector para backtesting, pesquisa de derivativos e análise comparativa de métricas históricas da Coinalyze. Selecione o tipo de mercado e a métrica antes de assinar e aplique um filtro de bolsa quando precisar reduzir o universo.

O adaptador é histórico e funciona somente por REST. Ele não fornece candles ao vivo, Level 1, negócios tick a tick, profundidade, portfólios nem execução de ordens. Símbolos, períodos, profundidade histórica e frequência de solicitações são definidos pela API Coinalyze.
