# Conector CoinGlass
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinGlass** integra o StockSharp à API de análise do mercado de criptomoedas da CoinGlass. Ele mapeia conjuntos selecionados de futuros, spot, opções, ETF de Bitcoin e ETF de Ethereum para instrumentos, mensagens de Level 1 e candles históricos do StockSharp.

## Principais recursos

- Seleção do tipo de mercado CoinGlass e restrição opcional por bolsa ou símbolo.
- Descoberta dos instrumentos disponíveis no conjunto configurado.
- Consulta de indicadores atuais de Level 1, como preço, volume, variação e interesse em aberto quando fornecidos.
- Atualização de snapshots de Level 1 em intervalo configurável.
- Download de séries históricas por período para preço, interesse em aberto, taxa de financiamento ou liquidações.
- Configuração de limite de até 1.000 registros históricos por solicitação.

## Uso típico

Use este conector em painéis de pesquisa, monitoramento de derivativos e análise histórica das métricas CoinGlass. Configure o token de API, o tipo de mercado e a métrica, restringindo bolsa ou símbolo quando precisar de um conjunto focado.

A CoinGlass é uma fonte de análise, não um ambiente de execução. O adaptador não fornece ordens, portfólios, negócios tick a tick nem profundidade. Eventos históricos de Level 1 e atualizações ao vivo de candles não são aceitos; solicitações de candles retornam somente histórico. Disponibilidade e limites dependem do plano da CoinGlass.
