# Conector de KyberSwap
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de KyberSwap** conecta StockSharp con KyberSwap Aggregator API v1 y redes EVM. Expone los pares de tokens configurados como instrumentos de StockSharp, obtiene cotizaciones ejecutables de las rutas del agregador y envía swaps en cadena firmados.

## Funciones principales

- Descubrimiento de pares de tokens configurados y sus metadatos en Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche y Linea.
- Cotizaciones de compra y venta de nivel 1 calculadas a partir de rutas ejecutables del agregador para un volumen de sondeo configurable.
- Sondeo REST periódico de las suscripciones activas de nivel 1; no hay eventos históricos de cotizaciones ni transporte de streaming.
- Swaps de mercado inmediatos, firmados localmente y transmitidos por JSON-RPC de EVM, con deslizamiento configurable y aprobación automática de tokens.
- Saldos de tokens del monedero y actualizaciones de cartera mediante llamadas a la cadena.
- Seguimiento por hash de los swaps enviados por el conector hasta que un recibo EVM confirme el éxito o el fallo.
- Sin operaciones por tick, libros, velas, órdenes limitadas ni modificación o cancelación de transacciones ya transmitidas.

## Uso habitual

Use este conector para supervisar cotizaciones DEX con conocimiento de las rutas y automatizar swaps de mercado en las redes EVM admitidas.

Las cotizaciones pueden consultarse sin credenciales de negociación, pero la ejecución requiere un monedero, una clave privada y un endpoint RPC operativo. Las definiciones de tokens, la liquidez de rutas, las aprobaciones, el gas, el deslizamiento, la latencia del recibo, los límites de API y el estado de la red afectan al resultado de cada swap.
