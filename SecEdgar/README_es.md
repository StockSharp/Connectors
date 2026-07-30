# Conector de SEC EDGAR
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de SEC EDGAR** ofrece a StockSharp acceso de solo lectura a los datos oficiales de presentaciones de la Comisión de Bolsa y Valores de Estados Unidos. Convierte emisores, documentos y hechos XBRL en valores, noticias y un tipo específico de mensajes fundamentales de StockSharp.

## Funciones principales

- Búsqueda de empresas por ticker o CIK mediante el catálogo de tickers de la SEC.
- Presentaciones como noticias de StockSharp, incluidos envíos recientes y un número configurable de archivos históricos.
- Filtros por formularios como 10-K, 10-Q, 8-K, 20-F, 40-F y 6-K.
- Hechos XBRL de empresas con filtros de fecha y cantidad mediante el tipo de datos Company Facts.
- Solicitudes REST finitas para recopilación histórica y actualización periódica; el adaptador no abre un flujo push.
- No requiere clave API, pero la política de la SEC exige un User-Agent identificable y un ritmo de solicitudes prudente.
- No proporciona precios, operaciones, libros, velas, carteras ni envío de órdenes.
- Los endpoints, CIK, archivos históricos y formatos de la SEC quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisar presentaciones, investigar fundamentos, filtrar emisores y crear conjuntos que combinen divulgaciones de la SEC con datos de mercado de otro conector.

La cobertura y la puntualidad dependen de lo publicado por la SEC. El ritmo, los límites de archivos y hechos y los filtros de formularios dependen de la configuración y de la política de acceso de la SEC.
