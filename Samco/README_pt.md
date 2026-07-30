# Conector Samco
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Samco** conecta o StockSharp à Samco Trade API para valores mobiliários e derivativos indianos. Ele expõe os serviços de mercado e negociação da corretora pelo modelo unificado de mensagens do StockSharp.

## Principais recursos

- Pesquisa de ações, futuros e opções suportados na NSE, BSE, NFO, BFO, CDS, MCX e MFO.
- Cotações de nível 1, negócios tick a tick e livros de cinco níveis em tempo real pelo feed Samco.
- Candles históricos seguidos de atualizações por streaming ou consultas REST.
- Envio e alteração de ordens limitadas e demais tipos suportados, além de cancelamento individual; não há cancelamento atômico em grupo.
- Limites de carteira, ativos, posições, ordens e negócios, com conciliação periódica do estado privado.
- WebSocket opcional com fallback REST e intervalos e endpoints configuráveis.
- Autenticação por token diário de sessão válido ou credenciais API Samco, sujeita às regras de sessão da corretora.
- Identificadores, sessões e formatos Samco ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais do mercado indiano, estratégias ao vivo, monitoramento de carteira e gestão de ordens conectados a uma conta Samco.

Cobertura, profundidade de cinco níveis, histórico, permissões, limites e duração da sessão dependem da Samco e da conta conectada.
