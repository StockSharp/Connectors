# Conector de CoinSpot
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de CoinSpot** integra StockSharp con la bolsa y bróker de criptomonedas al contado CoinSpot. Utiliza las API REST pública, de negociación y privada de solo lectura para datos, estado de cuenta y operaciones con órdenes.

## Funciones principales

- Descubrir mercados al contado y metadatos de instrumentos de CoinSpot.
- Solicitar instantáneas de Level 1, libro de órdenes y operaciones tick recientes.
- Mantener las suscripciones públicas mediante consultas REST con intervalo configurable.
- Enviar órdenes limitadas o de mercado de compra y venta.
- Cancelar una orden o grupos de órdenes abiertas coincidentes.
- Cargar saldos, estado de cartera, órdenes abiertas e históricas y operaciones propias.
- Configurar por separado los endpoints público, de negociación y privado de solo lectura.

## Uso habitual

Use este conector para vigilar el mercado al contado de CoinSpot y automatizar operaciones por REST. Los datos públicos no requieren autenticación; las funciones de cuenta y órdenes necesitan una clave y un secreto de CoinSpot con los permisos adecuados.

El adaptador no dispone de WebSocket ni ofrece velas, eventos históricos de Level 1 o libros históricos. Las actualizaciones públicas se obtienen por sondeo y el historial de operaciones recientes está limitado por la respuesta del proveedor. No admite sustitución atómica, órdenes condicionales, iceberg, post-only ni GTD.
