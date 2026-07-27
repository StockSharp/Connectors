# Conector Bit2Me
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Bit2Me** liga o StockSharp ao Bit2Me Pro, a plataforma de negociação à vista do provedor espanhol de ativos digitais. É indicado para sistemas que precisam de acesso direto a mercados de criptomoedas com liquidez em EUR por meio do modelo padrão de mensagens do StockSharp.

## Principais recursos

- Descoberta dos mercados à vista do Bit2Me Pro e das regras de preço, quantidade e ordem mínima.
- Instantâneos REST de cotações de nível 1 e do livro de ofertas de nível 2.
- Negócios públicos e atualizações completas do livro em tempo real via WebSocket.
- Negócios históricos e velas OHLCV nos intervalos publicados pelo Bit2Me.
- Envio de ordens a mercado, limitadas e stop-limit.
- Cancelamento e consulta de ordens e execuções.
- Saldos da carteira e valores bloqueados por ordens ativas.
- Endereços REST e WebSocket configuráveis para testes, roteamento ou mudanças de infraestrutura.

## Uso típico

Use o conector em robôs de negociação, terminais, coletores de dados, serviços de gestão de ordens e ferramentas de monitoramento para instrumentos à vista do Bit2Me Pro.

Os dados públicos de mercado não exigem credenciais. Negociação e operações de conta requerem uma chave de API e um segredo do Bit2Me com as permissões adequadas. Mercados, limites e recursos da conta são controlados pelo Bit2Me.
