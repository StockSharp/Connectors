# Conector BigONE

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector BigONE** integra StockSharp con los mercados spot y de contratos de BigONE. Un único adaptador cubre pares de criptomonedas y contratos perpetuos con margen en moneda o USDT.

## Funciones principales

- Descubrimiento de pares spot y contratos perpetuos disponibles.
- Cotizaciones Level 1, libros de órdenes, operaciones públicas y velas OHLCV.
- Flujos spot mediante JSON WebSocket y flujos URL dedicados para contratos.
- Historial de velas spot y snapshots REST actuales de ambos mercados.
- Saldos spot y de contratos, posiciones, órdenes y operaciones privadas.
- Órdenes market, limit, IOC, FOK, post-only, stop spot y reduce-only de contratos.
- Cancelación individual y por grupos.
- Direcciones configurables para REST spot/contratos y WebSocket público y privado.

## Uso

El conector sirve para robots, terminales, recopiladores de datos, supervisión y gestión de órdenes que combinen liquidez spot y derivados de BigONE.

Los datos públicos no requieren credenciales. La cuenta y la negociación requieren una clave API y un secreto de BigONE.
