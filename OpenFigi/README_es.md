# Conector de OpenFIGI
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de OpenFIGI** conecta StockSharp con un servicio de correspondencia de identificadores de instrumentos financieros y datos de referencia. Traduce los resultados específicos del proveedor al modelo unificado de instrumentos de StockSharp, permitiendo usar identificadores coherentes entre distintas fuentes de datos.

## Funciones principales

- Cobertura habitual: instrumentos financieros globales y metadatos de identificadores.
- Correspondencia por FIGI, ISIN, CUSIP, SEDOL, ticker u otro tipo de identificador de OpenFIGI.
- Búsqueda y filtrado por código de bolsa, MIC, moneda, sector de mercado y tipo de instrumento.
- Mensajes normalizados de instrumentos de StockSharp con datos de referencia e identificadores del proveedor.
- Este adaptador es de solo lectura: no proporciona flujos de precios ni enruta órdenes.
- El transporte REST, la paginación, la limitación de solicitudes y los formatos de respuesta del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para mantener datos maestros de instrumentos, enriquecer identificadores, conciliar datos entre proveedores e incorporar instrumentos a los flujos de StockSharp.

Las correspondencias, los resultados de búsqueda, los tamaños de página, los límites y la disponibilidad dependen de OpenFIGI y de si se ha configurado una clave de API.
