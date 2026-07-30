# Conector CoinPaprika
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinPaprika** integra o StockSharp à API de dados de criptomoedas da CoinPaprika. Ele oferece dados globais de referência das moedas ou mercados de uma bolsa selecionada, além de snapshots de ticker e candles históricos OHLCV.

## Principais recursos

- Descoberta global de moedas CoinPaprika ou restrição dos instrumentos a uma bolsa configurada.
- Escolha da moeda de cotação para consultas de ticker e candles.
- Recebimento de snapshots de Level 1 com preço, volume de 24 horas, variação e estado quando disponíveis.
- Atualização de Level 1 por consultas REST em intervalo configurável.
- Download de candles históricos OHLCV por período.
- Uso da API gratuita sem token ou configuração de token para o endpoint profissional e permissões ampliadas.
- Limitação das respostas históricas a um máximo configurável de 366 registros.

## Uso típico

Use este conector para dados de referência de criptomoedas, acompanhamento simples de preços e pesquisa histórica OHLCV. Escolha a descoberta global ou por bolsa e defina a moeda de cotação antes de solicitar os dados.

A CoinPaprika é uma agregadora de dados, não um ambiente de negociação. O adaptador não fornece ordens, portfólios, negócios tick a tick nem profundidade. Eventos históricos de Level 1 e atualizações ao vivo de candles não estão disponíveis. Histórico intradiário, cobertura, tamanho da resposta e limites dependem do plano e do token CoinPaprika.
