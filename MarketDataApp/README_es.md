# Conector de MarketData.app
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de MarketData.app** conecta StockSharp con un servicio profesional de datos de mercado. Traduce los datos específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas solicitudes y flujos de trabajo con distintas fuentes de datos.

## Funciones principales

- Cobertura habitual: acciones, ETF, opciones, índices y fondos.
- Búsqueda de instrumentos, incluidas cadenas de opciones, y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: instantáneas de cotizaciones de nivel 1 y velas.
- Solicitudes de velas históricas para gráficos, análisis y pruebas retrospectivas; el servicio no proporciona velas de opciones.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para alimentar gráficos, descubrir instrumentos y opciones, almacenar datos de mercado, realizar análisis e investigación y probar estrategias con datos del proveedor.

Los instrumentos, la profundidad histórica, los ajustes, los límites, los derechos de datos y la disponibilidad dependen de MarketData.app y del plan de API conectado.
