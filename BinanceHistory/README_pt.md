# Conector de dados históricos da Binance

O **conector de dados históricos da Binance** importa para o StockSharp os arquivos públicos de dados de mercado da Binance. Ele converte arquivos da bolsa e dados de referência para o modelo unificado de mensagens do StockSharp, permitindo armazenamento, análise e reprodução consistentes.

## Principais recursos

- Cobertura dos mercados à vista e de derivativos de ativos digitais.
- Pesquisa de instrumentos e dados de referência dos contratos.
- Cotações históricas de nível 1, negócios tick a tick, livros de ordens e candles por período.
- Downloads por intervalo de datas para preencher automaticamente o armazenamento de dados de mercado.
- Este adaptador é destinado a dados históricos e não oferece assinaturas em tempo real nem encaminhamento de ordens.
- Formatos de arquivo e identificadores da Binance são normalizados pela API padrão do StockSharp.

## Uso típico

Use-o para preencher o armazenamento local, corrigir lacunas em séries históricas e preparar dados para pesquisas e backtests de estratégias.

Instrumentos, arquivos, intervalos de datas e granularidade disponíveis dependem dos arquivos publicados pela Binance.
