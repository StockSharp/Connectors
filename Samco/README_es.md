# Conector de Samco
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Samco** conecta StockSharp con Samco Trade API para valores y derivados de la India. Expone los servicios de datos y negociación del bróker mediante el modelo unificado de mensajes de StockSharp.

## Funciones principales

- Búsqueda de acciones, futuros y opciones admitidos en NSE, BSE, NFO, BFO, CDS, MCX y MFO.
- Cotizaciones de nivel 1, operaciones tick a tick y libros de cinco niveles en tiempo real mediante el feed de Samco.
- Velas históricas con posteriores actualizaciones mediante streaming o consultas REST.
- Envío y modificación de órdenes limitadas y otras órdenes admitidas, además de cancelación individual; no se ofrece cancelación grupal atómica.
- Límites de cartera, tenencias, posiciones, órdenes y operaciones, con conciliación periódica del estado privado.
- Datos WebSocket opcionales, respaldo mediante REST e intervalos y endpoints configurables.
- La autenticación usa un token de sesión diario vigente o credenciales API de Samco, sujeta a las reglas de sesión del bróker.
- Los identificadores, las sesiones y los formatos de Samco quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales del mercado indio, estrategias en vivo, supervisión de carteras y gestión de órdenes conectadas a una cuenta Samco.

La cobertura, la profundidad de cinco niveles, el historial, los permisos, los límites y la duración de la sesión dependen de Samco y de la cuenta conectada.
