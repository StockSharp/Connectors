# Conector CoinSpot
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector CoinSpot** integra o StockSharp à bolsa e corretora de criptomoedas à vista CoinSpot. Ele usa as APIs REST pública, de negociação e privada somente leitura para dados de mercado, estado da conta e operações com ordens.

## Principais recursos

- Descoberta de mercados à vista e metadados dos instrumentos da CoinSpot.
- Consulta de snapshots de Level 1, livro de ofertas e negócios tick recentes.
- Atualização das assinaturas públicas por consultas REST em intervalo configurável.
- Envio de ordens limitadas ou a mercado de compra e venda.
- Cancelamento de uma ordem ou de grupos de ordens abertas correspondentes.
- Carregamento de saldos, estado do portfólio, ordens abertas e históricas e negócios próprios.
- Configuração separada dos endpoints público, de negociação e privado somente leitura.

## Uso típico

Use este conector para acompanhar o mercado à vista da CoinSpot e automatizar operações por REST. Os dados públicos dispensam autenticação; funções de conta e ordens exigem chave e segredo CoinSpot com as permissões adequadas.

O adaptador não possui fluxo WebSocket e não fornece candles, eventos históricos de Level 1 ou livros históricos. As atualizações públicas são obtidas por consulta, e o histórico de negócios recentes é limitado pela resposta do provedor. Não há substituição atômica nem ordens condicionais, iceberg, post-only ou GTD.
