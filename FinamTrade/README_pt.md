# Conector Finam Trade API

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Finam Trade API** liga aplicações StockSharp às contas de corretagem e aos dados de mercado fornecidos pela Finam. Instrumentos, cotações, ordens, negócios e o estado da carteira são convertidos para o modelo unificado de mensagens do StockSharp.

## Principais recursos

- Pesquisa de ações, títulos, moedas, fundos, futuros e opções disponíveis na Finam.
- Cotações de nível 1, livros de ofertas, negócios públicos e candles por período.
- Consulta de candles históricos e assinaturas de mercado em tempo real.
- Envio de ordens a mercado, limitadas, stop e stop-limit, além de cancelamento.
- Atualizações de ordens, negócios próprios, saldos em dinheiro e posições.
- Troca automática do segredo da API por um token de sessão de curta duração.
- Endereços REST e WebSocket configuráveis para gateways compatíveis e ambientes de teste.

## Uso típico

O conector pode ser usado em robôs de negociação, terminais, monitores de carteira e serviços de gerenciamento de ordens que precisem de uma única interface StockSharp para a Finam.

É necessário um segredo da Finam Trade API. Uma conta pode ser selecionada explicitamente; caso contrário, o conector usa a primeira conta disponível para o token. Os instrumentos seguem o formato Finam `ticker@MIC`. Mercados, profundidade do histórico, dados em tempo real, permissões de negociação e limites de requisição dependem da conta e das condições do serviço Finam.
