# Conector de Velodrome
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Velodrome** conecta StockSharp con los pools clásicos y Slipstream de Velodrome en Optimism. Convierte pools configurados, cotizaciones ejecutables, swaps en cadena, saldos y transacciones enviadas en mensajes de StockSharp.

## Funciones principales

- Descubrimiento de pools clásicos y de liquidez concentrada configurados, con metadatos de tokens.
- Precios bid y ask de nivel 1 derivados de sondeos ejecutables, con WebSocket y respaldo por sondeo.
- Operaciones tick a tick históricas y en vivo desde registros de swaps, con velas construidas a partir de esos eventos.
- Swaps de mercado inmediatos firmados con una clave EVM opcional, con gestión de allowances y deslizamiento configurable.
- Saldos de tokens, recibos de transacción y actualizaciones de órdenes y ejecuciones.
- La recopilación histórica está limitada por rangos y cantidades de bloques de Optimism configurados.
- No hay libro centralizado, órdenes limitadas en espera, sustitución atómica ni cancelación.
- RPC, unidades, variantes de pool, firma y registros quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisión DEX en Optimism, análisis de pools Velodrome, backtests por eventos, seguimiento de carteras y swaps directos.

La cobertura, los precios, la liquidez, el historial RPC, el gas, la finalidad y la disponibilidad dependen de Velodrome, Optimism y los servicios RPC.
