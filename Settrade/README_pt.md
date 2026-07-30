# Conector Settrade
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Settrade** conecta o StockSharp à Settrade Open API v2 para ações e derivativos tailandeses. Ele unifica os serviços REST e MQTT de mercado e corretagem no modelo de mensagens do StockSharp.

## Principais recursos

- Pesquisa direta por símbolo para a conta configurada de ações SET ou derivativos TFEX; não baixa o catálogo completo.
- Cotações de nível 1 e snapshots e atualizações do livro em tempo real; não oferece assinaturas de negócios tick a tick.
- Candles históricos seguidos de atualizações MQTT para os intervalos suportados.
- Ordens a mercado e limitadas, além de condicionais TFEX suportadas; contas de ações não expõem stops.
- Alteração e cancelamento com campos Settrade de validade, NVDR, iceberg, posição e gatilho quando aplicável.
- Informações da conta, carteiras, posições, ordens e negócios por snapshots, tópicos privados e conciliação periódica.
- Endpoints de produção e sandbox configuráveis; credenciais, Broker ID, conta, tipo e PIN são exigidos conforme a operação.
- Autenticação, tópicos MQTT e formatos Settrade ficam ocultos atrás da API padrão do StockSharp.

## Uso típico

Use-o em terminais do mercado tailandês, estratégias ao vivo, gestão de ordens e monitoramento de contas via Settrade.

Símbolos, intervalos, profundidade, funções, permissões e limites dependem da Settrade, do tipo de conta e de suas autorizações.
