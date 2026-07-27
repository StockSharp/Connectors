# Conector Dukascopy JForex

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [日本語](README_ja.md)

O **conector Dukascopy JForex** liga o StockSharp ao Dukascopy Bank por meio do SDK oficial JForex para Java. O SDK estabelece a sessão segura e autenticada com os servidores de negociação; o adaptador .NET troca comandos e eventos com ele por uma ponte exclusivamente local.

## Principais recursos

- Pesquisa de instrumentos de FX, CFD, metais, índices, commodities e títulos disponíveis para a conta.
- Cotações de nível 1, negócios tick a tick, alterações do livro e candles por período.
- Ticks e candles históricos pelos serviços de histórico JForex.
- Ordens a mercado, limitadas, stop, stop-limit e comandos específicos do JForex.
- Envio, alteração e cancelamento de ordens, execuções, saldos e posições.
- Endereços JForex separados e configuráveis para os ambientes demo e real.
- Inicialização da ponte a partir de um JAR executável indicado ou operação como processo local separado.

## Modelo de execução

Java é necessário porque a Dukascopy publica e mantém o JForex como API Java. O projeto Maven incluído usa o pacote oficial `DDS2-jClient-JForex`. A ponte escuta somente na interface loopback e não expõe credenciais da conta à rede.

O conector é indicado para robôs, terminais, monitoramento e gerenciamento de ordens usando o modelo padrão de mensagens StockSharp. Instrumentos, histórico, profundidade e permissões dependem da conta Dukascopy.
