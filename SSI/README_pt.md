# Conector SSI
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector SSI** conecta o StockSharp à SSI FastConnect API v3 para o mercado de valores vietnamita. Ele traduz dados de mercado e operações de corretagem da SSI para o modelo unificado de mensagens do StockSharp.

## Principais recursos

- Pesquisa de valores mobiliários e índices da HOSE, HNX e UPCOM, incluindo ações e futuros suportados.
- Cotações de nível 1, negócios tick a tick e livros de ordens em tempo real, com snapshots REST iniciais quando disponíveis.
- Candles históricos por período seguidos de atualizações por streaming nos intervalos suportados.
- Envio, substituição e cancelamento de ordens individuais, incluindo condições específicas da SSI.
- Pesquisa de contas e atualizações de saldos, posições, ordens e execuções por streaming e conciliação periódica.
- Endpoints REST e WebSocket e intervalo de consulta de carteira configuráveis.
- Credenciais FastConnect são obrigatórias; a negociação também depende de Client ID, conta, chave privada RSA e OTP atual.
- Sessões, formatos e tópicos da SSI ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais do mercado vietnamita, estratégias ao vivo, gestão de ordens e monitoramento com acesso direto à corretora SSI.

Instrumentos, profundidade histórica, permissões, limites e disponibilidade dependem da SSI e dos direitos da conta FastConnect conectada.
