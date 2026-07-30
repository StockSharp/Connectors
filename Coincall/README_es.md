# Conector de Coincall
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Coincall** integra StockSharp con opciones y futuros de Coincall. La configuración de producto selecciona la superficie de derivados; REST ofrece instantáneas e historia y las sesiones WebSocket autenticadas proporcionan actualizaciones en directo y privadas.

## Funciones principales

- Descubrir instrumentos de opciones o futuros de Coincall.
- Suscribirse a Level 1, profundidad, operaciones tick a tick y velas por intervalo.
- Descargar operaciones recientes y velas históricas antes de continuar con WebSocket.
- Enviar órdenes limitadas, de mercado y condicionales con precio de activación y parámetros compatibles GTC, IOC, FOK, post-only y reduce-only.
- Modificar o cancelar una orden y cancelar grupos de órdenes coincidentes.
- Cargar saldos, posiciones, órdenes abiertas e históricas y operaciones propias.
- Conciliar el estado privado con un intervalo configurable.

## Uso habitual

Use este conector para vigilar derivados y automatizar la negociación de opciones o futuros en Coincall. El descubrimiento y las instantáneas REST pueden conectarse sin credenciales, pero el WebSocket y todas las funciones privadas requieren una clave y un secreto de API.

Cada instancia selecciona una sola superficie de producto. No se admiten órdenes iceberg ni vencimientos absolutos; los libros se entregan como instantáneas y no hay registro de órdenes. Los instrumentos, permisos de negociación y límites de API dependen de Coincall.
