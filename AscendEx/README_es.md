# Conector AscendEX

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector AscendEX** integra StockSharp con la API AscendEX Pro publicada. Un único adaptador cubre mercados spot cash, margen y futuros perpetuos, por lo que resulta útil para estrategias criptográficas multimarco y para conservar la implementación del protocolo documentado de la plataforma.

## Capacidades principales

- Descubrimiento de instrumentos spot, de margen y de futuros perpetuos con estado, pasos de precio y volumen y límites de órdenes.
- Cotizaciones de nivel 1, libros de nivel 2, operaciones públicas y velas OHLCV.
- Instantáneas e historial por REST y WebSockets separados en tiempo real para spot y futuros.
- Saldos cash y margin, garantías y posiciones de futuros, órdenes abiertas e históricas y ejecuciones.
- Órdenes market, limit, stop-market y stop-limit con GTC, IOC, FOK, post-only y reduce-only para futuros.
- Cancelación individual y masiva de órdenes.
- Direcciones configurables para REST y ambos WebSockets, grupo de cuenta y modo cash o margin.

Los datos públicos no requieren credenciales. Las funciones de cartera y negociación requieren clave API, secreto y el grupo de cuenta asignado por AscendEX.
