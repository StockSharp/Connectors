# Conector PrizmBit
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector PrizmBit** conecta o StockSharp a uma integração legada com uma exchange de ativos digitais. Ele traduz dados e operações específicos do provedor para o modelo unificado de mensagens do StockSharp, permitindo usar as mesmas assinaturas e fluxos em diferentes mercados.

O serviço original pode não estar mais disponível. A integração é mantida para compatibilidade, manutenção de sistemas existentes e estudo de uma implementação completa de conector.

## Principais recursos

- Cobertura típica: ativos digitais.
- Pesquisa de instrumentos e dados de referência do provedor.
- Dados de mercado suportados pelo adaptador: cotações de nível 1, negócios tick a tick, livros de ordens, candles e eventos do log de ordens.
- Fluxos de envio de ordens e execuções suportados pelo provedor.
- Atualizações de carteiras, saldos, posições e estado das execuções.
- Assinaturas em tempo real pelo transporte de streaming do provedor.
- Transportes, sessões e formatos específicos do provedor ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o para manter uma integração existente ou como código-fonte prático para aprender a mapear dados, transações e detalhes de protocolo para o StockSharp.

Antes do uso operacional, confirme se a API original e os endereços necessários continuam disponíveis.
