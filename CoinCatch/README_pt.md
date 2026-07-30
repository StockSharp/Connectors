# Conector CoinCatch
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinCatch** integra o StockSharp aos mercados à vista e de derivativos da CoinCatch. A configuração de produto seleciona spot, futuros com margem em USDT ou futuros com margem em moeda, enquanto as APIs REST e WebSocket fornecem dados e negociação autenticada.

## Principais recursos

- Descoberta de instrumentos do produto CoinCatch selecionado.
- Assinatura de Level 1, profundidade de mercado, negócios tick a tick e candles por período.
- Download de candles históricos com continuação por atualizações WebSocket em tempo real.
- Envio de ordens limitadas e a mercado, incluindo reduce-only em futuros e post-only em ordens limitadas.
- Cancelamento de uma ordem ou de todas as ordens de um símbolo.
- Carregamento de saldos, posições, ordens abertas e históricas e negócios próprios.
- Reconciliação do estado privado com chave, segredo e frase secreta de API.

## Uso típico

Use este conector para acompanhar mercados à vista ou futuros, obter histórico de candles e automatizar operações na CoinCatch. Selecione o produto antes de conectar e informe credenciais com permissões adequadas de leitura ou negociação para operações privadas.

O adaptador não expõe ordens planejadas ou de gatilho da CoinCatch, ordens iceberg nem substituição atômica. O livro é entregue por snapshots e não há fluxo de log de ordens. Devem ser respeitados as regras dos instrumentos, o modo da conta, as permissões e os limites da bolsa.
