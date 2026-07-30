# Conector de IIFL
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de IIFL** conecta StockSharp con IIFL Markets Open API para obtener datos de los mercados indios y realizar operaciones de corretaje. Convierte los servicios REST y MQTT de IIFL al modelo de mensajes estándar de StockSharp.

## Funciones principales

- Descubrimiento de instrumentos en NSE, BSE y segmentos de derivados de renta variable, divisas y materias primas, incluidos acciones, índices, futuros y opciones.
- Instantáneas de nivel 1, libros de cinco niveles y actualizaciones de operaciones por tick mediante REST y el flujo MQTT oficial.
- Velas históricas y actualizaciones por sondeo de 1, 5, 10, 15 y 30 minutos; 1 hora; 1 día; 1 semana; y 1 mes.
- Órdenes de mercado, limitadas, stop limitadas y stop de mercado, con modificación y cancelación individual; no se admite la cancelación masiva.
- Productos y complejidades de órdenes específicos de IIFL, precios de activación, volumen divulgado, protección de mercado, identificadores de algoritmos y etiquetas de cliente.
- Fondos, tenencias y posiciones de cartera, estado de órdenes y ejecuciones mediante instantáneas REST y actualizaciones MQTT privadas.
- Streaming MQTT configurable y sondeo REST para el estado privado y las velas activas.

## Uso habitual

Use este conector para terminales del mercado indio, estrategias en vivo, servicios de gestión de órdenes, supervisión de carteras y análisis basado en velas.

La conexión requiere credenciales de aplicación de IIFL, un identificador de cliente y el flujo de autorización diario o un token de sesión existente. Los instrumentos, permisos de datos, funciones de órdenes, horarios y límites de solicitudes dependen de la cuenta IIFL y del segmento bursátil.
