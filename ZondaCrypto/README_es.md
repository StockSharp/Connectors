# Conector de Zonda Crypto
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Zonda Crypto** conecta StockSharp con el exchange centralizado de criptomonedas al contado zondacrypto. Traduce los datos REST y WebSocket y las operaciones de cuenta al modelo unificado de mensajes de StockSharp.

## Funciones principales

- Descubrimiento de mercados al contado con moneda, incrementos de precio y cantidad e importe mínimo.
- Nivel 1, operaciones tick a tick e instantáneas y actualizaciones del libro en tiempo real mediante flujos públicos.
- Instantáneas REST e historial reciente disponible antes de continuar en vivo; no ofrece velas.
- Órdenes de mercado y limitadas con opciones GTC, IOC, FOK y post-only admitidas.
- Cancelación individual o grupal filtrada y actualizaciones de órdenes y ejecuciones; no hay sustitución atómica.
- Saldos de cartera mediante flujos privados y conciliación REST periódica.
- Las operaciones privadas requieren clave y secreto API; los datos públicos no necesitan credenciales de negociación.
- La autenticación, los códigos, transportes, filtros y formatos de zondacrypto quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales al contado de zondacrypto, estrategias en vivo, análisis de operaciones recientes, supervisión de cuentas y gestión de órdenes.

Los mercados, el historial reciente, los permisos, las opciones de orden, los límites y la disponibilidad dependen de zondacrypto y de la cuenta conectada.
