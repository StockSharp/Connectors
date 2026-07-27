# Conector CoinTR

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinTR** liga o StockSharp à CoinTR, uma bolsa de criptomoedas focada no mercado turco. Ele disponibiliza os instrumentos spot da CoinTR por meio do modelo padrão de mensagens do StockSharp.

## Principais recursos

- Descoberta de instrumentos spot com precisão de preço e quantidade e limites de negociação.
- Cotações de nível 1, snapshots do livro de ofertas de nível 2 e negócios públicos.
- Tickers, livros, negócios e candles em tempo real via WebSocket.
- Candles OHLCV históricos nos intervalos suportados pela CoinTR.
- Saldos da carteira, ordens ativas e notificações privadas de execuções.
- Envio de ordens a mercado, limitadas e por gatilho e cancelamento de ordens.
- Endereços REST e WebSocket público e privado configuráveis.

## Uso típico

Use o conector em robôs de negociação, terminais, coletores de dados, ferramentas de monitoramento e serviços de gestão de ordens nos mercados spot da CoinTR.

Os dados públicos não exigem credenciais. A negociação e o acesso à conta exigem chave de API, segredo e frase secreta com as permissões adequadas. Em uma compra a mercado, a CoinTR interpreta o volume como um valor na moeda de cotação; ordens limitadas e vendas a mercado usam a quantidade do ativo-base.
