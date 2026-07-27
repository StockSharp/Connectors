# Conector de GMTrade
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de GMTrade** conecta StockSharp con un protocolo de negociación y liquidez en cadena. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: activos en cadena y pools de liquidez, derivados.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick y velas.
- Solicitudes de datos históricos para gráficos, análisis y pruebas retrospectivas.
- Actualizaciones de carteras, saldos, posiciones y estado de ejecuciones.
- Suscripciones en tiempo real mediante el transporte de streaming del proveedor.
- Este adaptador está destinado a datos de mercado y no enruta órdenes.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para alimentar gráficos, almacenamiento de mercado, análisis, investigación y pruebas de estrategias con datos del proveedor.

Las redes, pools, instrumentos y profundidad de datos disponibles dependen de GMTrade y de los servicios RPC o de indexación configurados.
