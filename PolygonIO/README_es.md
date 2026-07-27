# Conector de Polygon.io
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Polygon.io** conecta StockSharp con un servicio profesional de datos y análisis de mercado. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: acciones, futuros, opciones, Forex y CFD, índices.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick, velas y noticias financieras.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Suscripciones en tiempo real mediante el transporte de streaming del proveedor.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para alimentar gráficos, almacenamiento de mercado, análisis, investigación y pruebas de estrategias con datos del proveedor.

Los instrumentos, la profundidad de datos, los permisos de negociación, los límites y la disponibilidad dependen de Polygon.io, del plan de API y de la cuenta conectada.
