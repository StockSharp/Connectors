# Conector de Marketstack
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Marketstack** conecta StockSharp con un servicio profesional de datos y análisis de mercado. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: acciones.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1 y velas.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para alimentar gráficos, almacenamiento de mercado, análisis, investigación y pruebas de estrategias con datos del proveedor.

Los instrumentos, la profundidad de datos, los permisos de negociación, los límites y la disponibilidad dependen de Marketstack, del plan de API y de la cuenta conectada.
