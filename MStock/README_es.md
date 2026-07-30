# Conector de m.Stock
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de m.Stock** conecta StockSharp con un bróker indio y los segmentos de mercado que admite. Traduce los datos y las operaciones específicos del proveedor al modelo unificado de mensajes de StockSharp, permitiendo usar las mismas suscripciones y flujos de trabajo en distintos mercados.

## Funciones principales

- Cobertura habitual: acciones, índices, futuros, opciones, derivados de divisas, fondos y bonos de la India.
- Búsqueda de instrumentos y datos de referencia del proveedor.
- Datos de mercado admitidos por el adaptador: cotizaciones de nivel 1, operaciones tick a tick, libros de órdenes y velas.
- Solicitudes de velas históricas para gráficos, análisis y pruebas retrospectivas.
- Flujos admitidos por el proveedor para enviar, modificar y cancelar órdenes, así como procesar ejecuciones.
- Actualizaciones de carteras, saldos, posiciones, órdenes y operaciones.
- Suscripciones en tiempo real mediante el transporte de streaming del proveedor.
- El transporte, las sesiones y los formatos del proveedor quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para estrategias en vivo, terminales, servicios de gestión de órdenes y herramientas de supervisión que necesiten acceso directo a una cuenta de m.Stock.

Los instrumentos, segmentos de mercado, profundidad de datos, permisos de negociación, límites y disponibilidad dependen de m.Stock, de las bolsas y de la cuenta conectada.
