# Conector TraderMade
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector TraderMade** conecta o StockSharp aos serviços de dados de câmbio e criptomoedas da TraderMade. Ele converte histórico REST e cotações WebSocket para o modelo unificado de mercado do StockSharp.

## Principais recursos

- Descoberta de pares pela lista de moedas e moedas de cotação configuradas, ou por uma lista explícita de símbolos.
- Preços bid, ask e médio de nível 1 em tempo real pela API de streaming.
- Livro TraderLadder opcional quando a conta possui acesso e o recurso está ativado.
- Candles históricos por período via REST, incluindo dados opcionais de criptomoedas nos fins de semana.
- Chaves REST e streaming separadas permitem configurações somente históricas, somente ao vivo ou combinadas.
- As assinaturas de candles são históricas e finitas; não há candles ao vivo nem negócios tick a tick.
- É um conector apenas de dados, sem carteiras, saldos ou envio de ordens.
- Símbolos, transportes e formatos TraderMade ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em painéis de câmbio e cripto, monitoramento de cotações, gráficos, análises e backtests sem execução por corretora.

Pares, TraderLadder, intervalos, histórico, limites, dados de fim de semana e streaming dependem da TraderMade e do plano API.
