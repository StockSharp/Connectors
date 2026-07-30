# Conector de SimFin
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de SimFin** ofrece a StockSharp acceso de solo lectura a fundamentos empresariales e historial diario de precios de SimFin. Convierte los registros en valores, instantáneas de nivel 1, velas diarias y un tipo específico de mensajes fundamentales de StockSharp.

## Funciones principales

- Búsqueda de empresas y valores por ticker o identificador de empresa de SimFin.
- Último registro diario disponible como instantánea de nivel 1.
- Velas OHLCV diarias históricas; no admite intervalos intradía ni actualizaciones de velas en vivo.
- Estados configurables de resultados, balance, flujo de efectivo y métricas derivadas.
- Controles de período fiscal, fechas, valores normalizados o declarados, ratios y máximo de registros.
- Suscripciones REST finitas para investigación e historia; no existe transporte de streaming.
- No proporciona ticks, libros, noticias, carteras ni operaciones de negociación.
- La autenticación, el control de frecuencia y los formatos de SimFin quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para selección fundamental, valoración, análisis diario y backtests que combinen SimFin con ejecución o datos intradía de otro conector.

Las empresas, los campos, el historial, la frecuencia, los límites y el acceso dependen de SimFin y del plan API conectado.
