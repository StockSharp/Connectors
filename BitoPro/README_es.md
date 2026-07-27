# Conector BitoPro

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector BitoPro** conecta StockSharp con BitoPro, un exchange de criptomonedas regulado y orientado a Taiwán, con mercados spot activos en TWD.

## Capacidades principales

- Descubrimiento de instrumentos spot, precisión de precio y cantidad y límites de negociación.
- Datos Level 1, instantáneas del libro Level 2 y operaciones públicas.
- Tickers, libros y operaciones en tiempo real mediante WebSocket.
- Velas OHLCV históricas para todos los intervalos ofrecidos por BitoPro.
- Saldos, órdenes abiertas e históricas e historial de operaciones privadas.
- Órdenes limit, market, stop-limit y post-only, con cancelación individual y masiva.
- Direcciones REST y WebSocket configurables.

## Uso habitual

Puede utilizarse en robots, terminales, recolectores de datos de mercados TWD, sistemas de supervisión y gestión de órdenes.

Los datos públicos no requieren credenciales. Las operaciones de cuenta y trading requieren correo electrónico, clave API y secreto. BitoPro recibe las compras market en la divisa cotizada; el conector convierte el volumen base de StockSharp con el último precio público.
