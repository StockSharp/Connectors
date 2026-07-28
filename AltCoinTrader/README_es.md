# Conector AltCoinTrader

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector AltCoinTrader** integra StockSharp con el mercado spot sudafricano de AltCoinTrader. Sus libros denominados en ZAR son útiles para descubrir precios locales, supervisar el mercado, recopilar datos y automatizar operaciones con criptomonedas.

## Funciones principales

- Descubrimiento de instrumentos spot con estado de negociación, precisión de precio y cantidad y valor mínimo de orden.
- Cotizaciones Level 1, libros Level 2 y operaciones públicas.
- Ticker, profundidad y operaciones en tiempo real mediante el WebSocket público.
- Instantáneas de mercado y operaciones públicas recientes mediante REST.
- Saldos, órdenes abiertas e históricas, ejecuciones privadas y actualizaciones de cuenta mediante el WebSocket autenticado.
- Órdenes límite con GTC, IOC y FOK, órdenes de mercado y cancelación individual o masiva con filtros.
- Direcciones de servicio REST y WebSocket configurables.

Los datos públicos están disponibles sin credenciales. Las funciones de cartera y negociación requieren una clave API y un secreto de AltCoinTrader con los permisos adecuados.
