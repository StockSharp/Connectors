# Conector Coinstore

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector Coinstore** conecta StockSharp con el mercado spot de criptomonedas de Coinstore. Es útil para seguir su amplio mercado de listados y automatizar operaciones en pares de criptomonedas y stablecoins.

## Funciones principales

- Descubrimiento de instrumentos spot con estado, precisión de precio y cantidad y mínimos de orden.
- Datos Level 1, libros Level 2, operaciones públicas y velas OHLCV.
- Ticker, profundidad, operaciones y velas en tiempo real mediante WebSocket.
- Operaciones recientes, instantáneas del libro e historial de velas mediante REST.
- Saldos, órdenes activas, estado de órdenes y ejecuciones privadas.
- Órdenes de mercado, límite, post-only e IOC, con cancelación individual y masiva.
- Direcciones REST y WebSocket configurables.

Los datos públicos no requieren credenciales. Las funciones de cartera y negociación requieren una clave API y un secreto de Coinstore. El estado privado se actualiza mediante solicitudes REST autenticadas.
