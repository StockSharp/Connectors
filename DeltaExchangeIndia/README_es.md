# Conector de Delta Exchange India
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Delta Exchange India** conecta StockSharp con una plataforma india centralizada de derivados sobre activos digitales. Convierte los datos de mercado de futuros y opciones, las órdenes y el estado de la cuenta al modelo de mensajes estándar de StockSharp.

## Funciones principales

- Descubrimiento y datos de referencia de los futuros y opciones admitidos por Delta Exchange India.
- Instantáneas de nivel 1 mediante REST y actualizaciones en tiempo real por WebSocket; no hay eventos históricos de nivel 1.
- Historial reciente de operaciones mediante REST, limitado a 50 operaciones por solicitud, y operaciones en vivo por WebSocket.
- Instantáneas y actualizaciones en tiempo real del libro con hasta 15 niveles; no se admiten libros incrementales ni históricos.
- Velas históricas, hasta 1.999 barras por solicitud, y actualizaciones de velas en vivo para los intervalos admitidos por el proveedor.
- Órdenes limitadas, de mercado y stop condicionales, con post-only, reduce-only, modificación, cancelación y cancelación masiva.
- Actualizaciones de cartera, saldos, posiciones, órdenes y ejecuciones mediante REST autenticado y canales privados.

## Uso habitual

Use este conector para estrategias de derivados en vivo, terminales de negociación, servicios de gestión de órdenes y análisis que requieran operaciones recientes o historial de velas de Delta Exchange India.

Las operaciones privadas requieren credenciales de API y los permisos de cuenta necesarios. Los instrumentos disponibles, el alcance histórico, los límites de solicitudes y la disponibilidad regional dependen del proveedor; no se implementan órdenes iceberg, vencimiento absoluto ni cierre masivo de posiciones.
