# Conector de dados históricos da KuCoin

O **conector de dados históricos da KuCoin** importa para o StockSharp os arquivos públicos de dados de mercado da KuCoin. Ele normaliza os dados baixáveis dos mercados à vista e de futuros no modelo unificado de mensagens do StockSharp.

## Principais recursos

- Pesquisa de instrumentos e dados de referência para os mercados à vista e de futuros.
- Negócios históricos tick a tick, livros de ordens e candles por período.
- Downloads por intervalo de datas para preencher o armazenamento de dados de mercado de forma reproduzível.
- Símbolos da bolsa e segmentos de mercado são mapeados para identificadores de instrumentos do StockSharp.
- Este adaptador é destinado a dados históricos e não oferece assinaturas em tempo real nem encaminhamento de ordens.
- Transporte e formatos de arquivo da KuCoin ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para preparar históricos da KuCoin destinados a gráficos, análises, reprodução de mercado e backtests de estratégias.

Instrumentos, arquivos, datas, profundidades e intervalos de candles disponíveis dependem dos conjuntos de dados públicos mantidos pela KuCoin.
