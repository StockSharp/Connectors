# Conector CoinSwitch
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinSwitch** integra o StockSharp às APIs CoinSwitch PRO. A configuração de produto seleciona mercados spot em INR ou USDT, futuros perpétuos com margem em USDT ou a interface HFT de opções em beta privada.

## Principais recursos

- Descoberta de instrumentos do produto CoinSwitch selecionado.
- Assinatura de Level 1, profundidade, negócios tick a tick e candles por período.
- Download de histórico de candles e atualizações por WebSocket quando o produto e o período oferecerem suporte.
- Envio de ordens limitadas no spot; limitadas, a mercado ou stop-market em futuros; e limitadas ou a mercado em opções HFT.
- Uso de reduce-only em ordens de derivativos aceitas e dos modos de validade disponíveis para opções HFT.
- Cancelamento de uma ordem ou de grupos de ordens correspondentes.
- Carregamento de saldos, posições, ordens abertas e históricas e negócios próprios.

## Uso típico

Use este conector para acompanhar a CoinSwitch PRO e automatizar operações em uma superfície de produto selecionada. Operações privadas exigem chave de API e segredo Ed25519 com permissões adequadas; opções também requerem acesso à beta privada HFT da CoinSwitch.

Os recursos variam: spot aceita somente ordens limitadas, entrada condicional existe apenas em futuros como stop-market e candles de opções não usam WebSocket. Não há substituição atômica, ordens iceberg ou GTD, livros incrementais nem fluxo de log de ordens. Aplicam-se as permissões e os limites da CoinSwitch.
