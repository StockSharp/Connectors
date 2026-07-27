# Conector de RavenPack
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de RavenPack** conecta StockSharp con un servicio de noticias financieras y datos de eventos. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: acciones, Forex y CFD, materias primas, índices.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: noticias financieras.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para incorporar noticias y eventos del proveedor a sistemas de supervisión, análisis, alertas y estrategias basadas en eventos.

Los instrumentos, la profundidad de datos, los permisos de negociación, los límites y la disponibilidad dependen de RavenPack, del plan de API y de la cuenta conectada.
