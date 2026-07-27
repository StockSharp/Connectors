# Conector BtcTurk

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector BtcTurk** liga o StockSharp à BtcTurk Kripto, uma bolsa turca de criptomoedas à vista. Ele é indicado para sistemas de negociação e de dados de mercado que trabalham com mercados em TRY, BTC, USDT e outras moedas por meio do modelo de mensagens padrão do StockSharp.

## Principais recursos

- Consulta de instrumentos à vista e dos limites de preço, volume e ordens.
- Cotações de nível 1, snapshots do livro de nível 2 e negócios públicos.
- Tickers, livros de ofertas e negócios em tempo real por WebSocket.
- Velas históricas OHLCV nos intervalos suportados pela BtcTurk.
- Saldos da carteira, ordens abertas e históricas e negócios da conta.
- Envio de ordens a mercado, limite, stop a mercado e stop limite.
- Cancelamento de uma ordem ou de grupos de ordens.
- Endereços REST, de histórico e WebSocket configuráveis.

## Uso típico

Use o conector em robôs de negociação, terminais, coletores de dados e sistemas de gestão de ordens ou monitoramento para os mercados à vista da BtcTurk Kripto.

Dados públicos não exigem credenciais. Para negociar e acessar a conta são necessários uma chave de API da BtcTurk e um segredo codificado em Base64 com as permissões adequadas. Em uma compra a mercado, a BtcTurk interpreta a quantidade como um valor na moeda de cotação; nas demais ordens ela é expressa no ativo-base.
