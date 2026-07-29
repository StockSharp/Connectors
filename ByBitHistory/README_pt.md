# Conector de dados históricos da Bybit

O **conector de dados históricos da Bybit** importa para o StockSharp os arquivos públicos de dados de mercado da Bybit. Ele normaliza os dados baixáveis de instrumentos à vista e derivativos no modelo padrão de mensagens do StockSharp.

## Principais recursos

- Pesquisa de instrumentos dos mercados à vista, lineares, inversos e de opções.
- Negócios históricos tick a tick para instrumentos à vista e derivativos suportados.
- Dados históricos incrementais de livros de ordens para mercados e profundidades suportados.
- Downloads por intervalo de datas para cargas em massa e conjuntos de pesquisa reproduzíveis.
- Este adaptador é destinado a dados históricos e não oferece assinaturas em tempo real nem encaminhamento de ordens.
- Formatos de arquivo e identificadores de mercado da Bybit ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para criar históricos de negócios e livros de ordens destinados a análises, reprodução de mercado e backtests de estratégias.

Instrumentos, datas, profundidades de livro e arquivos disponíveis dependem dos conjuntos de dados públicos mantidos pela Bybit.
