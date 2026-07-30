# Conector de DexScreener
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de DexScreener** incorpora a StockSharp análisis de pares de exchanges descentralizados en varias cadenas mediante la API REST pública de DexScreener. Es un adaptador de datos de mercado de solo lectura y no requiere credenciales de API.

## Funciones principales

- Descubrimiento de pares por identificador de cadena, dirección de token, dirección exacta del par o búsqueda de texto, con límites de omisión y cantidad de StockSharp.
- Instantáneas de nivel 1 con los últimos precios en USD y en el token nativo, volumen y variación de precio de 24 horas, liquidez y estado de negociación.
- Actualización periódica por REST de las suscripciones activas de nivel 1; el intervalo es configurable y su valor predeterminado es de 30 segundos.
- Cobertura de las cadenas y los fondos de liquidez indexados por DexScreener.
- Acceso público sin clave de API ni sesión privada de cuenta.
- Sin eventos históricos de nivel 1 ni transporte de streaming en tiempo real.
- Sin operaciones por tick, libros de órdenes, velas, envío de órdenes, datos de cartera ni operaciones de cuenta.

## Uso habitual

Use este conector para descubrir pares DEX, crear listas de seguimiento, filtrar por liquidez y alimentar paneles que necesiten métricas de mercado agregadas con actualización periódica.

No es un conector de ejecución ni una fuente de historial de eventos apta para backtesting. La cobertura de pares, los campos disponibles, la actualidad de los datos y los límites de solicitudes dependen de DexScreener y de los mercados descentralizados subyacentes.
