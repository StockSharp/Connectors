# Conector do protocolo FIX

O **conector do protocolo FIX** conecta o StockSharp a corretoras, bolsas e sistemas de negociação por meio de sessões configuráveis do Financial Information eXchange. Ele mapeia mensagens específicas de cada dialeto para o modelo unificado de mensagens do StockSharp.

## Principais recursos

- Dialetos FIX configuráveis para diferentes corretoras, mercados e segmentos.
- Login de sessão, autenticação, heartbeat, controle de sequência, reenvio, reconexão e transporte seguro opcional.
- Pesquisa de instrumentos e dados como nível 1, negócios, livros de ordens, candles, notícias e eventos do log de ordens quando suportados pelo dialeto.
- Envio, alteração e cancelamento de ordens, cancelamento em massa, consulta de estado e processamento de execuções quando suportados pela contraparte.
- Atualizações de carteiras, saldos e posições para sessões transacionais.
- Configuração de remetente, destino, conta, endpoint e sessão pelo modelo padrão do StockSharp.

## Uso típico

Use-o para integrações personalizadas com corretoras, gateways de bolsa, estratégias ao vivo, serviços de gestão de ordens e acesso normalizado a dados de mercado.

Mensagens, campos, tipos de ordem, recuperação e permissões dependem do dialeto FIX selecionado e da especificação de sessão da contraparte.
