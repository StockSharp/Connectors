# Conector de dados históricos da Gate.io

O **conector de dados históricos da Gate.io** importa para o StockSharp os arquivos públicos de dados de mercado da Gate.io. Ele converte conjuntos de dados à vista e de derivativos para o modelo unificado de mensagens do StockSharp, permitindo armazenamento, análise e reprodução.

## Principais recursos

- Pesquisa de instrumentos dos mercados à vista, de futuros perpétuos e de futuros com entrega.
- Negócios históricos tick a tick, livros de ordens incrementais e candles por período.
- Downloads por intervalo de datas para preenchimento sistemático dos dados de mercado.
- Símbolos nativos e variantes de mercado são mapeados para identificadores de instrumentos do StockSharp.
- Este adaptador é destinado a dados históricos e não oferece assinaturas em tempo real nem encaminhamento de ordens.
- Formatos de arquivo da Gate.io ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para preparar históricos de criptoativos destinados a gráficos, análises, pesquisa de livros de ordens e backtests de estratégias.

Instrumentos, arquivos, datas, profundidades e intervalos de candles disponíveis dependem dos conjuntos de dados públicos publicados pela Gate.io.
