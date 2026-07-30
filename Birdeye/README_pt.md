# Conector Birdeye
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Birdeye** integra o StockSharp às APIs de dados de criptomoedas on-chain da Birdeye. Ele oferece descoberta de tokens, indicadores atuais de mercado e histórico OHLCV para uma blockchain selecionada, usando Solana como padrão.

## Principais recursos

- Descoberta de tokens e carregamento de dados de referência da rede selecionada.
- Restrição da busca por endereço do token e aplicação de filtro de liquidez mínima.
- Obtenção de snapshots de Level 1 e atualização por consultas periódicas à API REST.
- Download de candles históricos por período, respeitando o limite de histórico configurado.
- Ativação do WebSocket pago para atualizações em tempo real de Level 1 e candles.
- Preços expressos em dólares americanos ou na moeda nativa da rede.
- Uso dos intervalos aceitos pela Birdeye; candles inferiores a um minuto estão disponíveis apenas na Solana.

## Uso típico

Use este conector para seleção de tokens, monitoramento de preços on-chain e análise de histórico OHLCV nas redes aceitas pela Birdeye. Configure a rede, o token de API, a moeda de cotação e os filtros opcionais antes de assinar os dados.

A Birdeye é uma provedora de dados de mercado, portanto o conector não oferece ordens, portfólios, execuções nem livro de ofertas. Eventos históricos de Level 1 não estão disponíveis e, sem streaming, a assinatura de candles termina após a resposta histórica. Cobertura, acesso ao WebSocket e limites de requisição dependem do plano de API da Birdeye.
