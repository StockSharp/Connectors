# Conector Tokocrypto

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector Tokocrypto** integra StockSharp con el mercado al contado MAIN de Tokocrypto. Está pensado para el comercio de criptomonedas orientado a Indonesia y para aplicaciones que necesitan datos de Tokocrypto en el modelo de mensajes de StockSharp.

## Funciones principales

- Descubrimiento de instrumentos spot MAIN con filtros de precio, volumen y orden mínima.
- Cotizaciones de nivel 1, libros de nivel 2, operaciones públicas y velas OHLCV.
- Tickers, libros parciales, operaciones y velas en tiempo real mediante WebSocket.
- Velas históricas y capturas recientes del mercado mediante la API REST pública.
- Saldos spot, órdenes abiertas e históricas e historial de ejecuciones privadas.
- Órdenes de mercado, límite, stop-market, stop-limit, post-only, IOC y FOK.
- Cancelación individual y por grupo; las direcciones REST de cuenta, REST de mercado y WebSocket son configurables.

## Uso habitual

Puede utilizarse en robots de negociación, terminales, recopiladores de datos, servicios de supervisión y sistemas de gestión de órdenes para instrumentos spot de Tokocrypto.

Los datos públicos no requieren credenciales. Las operaciones de cuenta y negociación requieren una clave API y un secreto de Tokocrypto.
