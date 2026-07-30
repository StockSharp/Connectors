# Conector de Settrade
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Settrade** conecta StockSharp con Settrade Open API v2 para acciones y derivados tailandeses. Unifica los servicios REST y MQTT de mercado y corretaje bajo el modelo de mensajes de StockSharp.

## Funciones principales

- Búsqueda directa por símbolo para la cuenta configurada de acciones SET o derivados TFEX; no descarga el catálogo completo.
- Cotizaciones de nivel 1 e instantáneas y actualizaciones del libro en tiempo real; no ofrece suscripciones a operaciones tick a tick.
- Velas históricas con posteriores actualizaciones MQTT para los intervalos admitidos.
- Órdenes de mercado y limitadas, además de órdenes condicionales TFEX admitidas; las cuentas de acciones no exponen stops.
- Modificación y cancelación con campos Settrade de validez, NVDR, iceberg, posición y activación cuando corresponda.
- Información de cuenta, carteras, posiciones, órdenes y operaciones mediante instantáneas, temas privados y conciliación periódica.
- Endpoints de producción y sandbox configurables; se requieren credenciales, Broker ID, cuenta, tipo y PIN según la operación.
- La autenticación, los temas MQTT y los formatos de Settrade quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales del mercado tailandés, estrategias en vivo, gestión de órdenes y supervisión de cuentas a través de Settrade.

Los símbolos, intervalos, profundidad, funciones de cuenta, permisos y límites dependen de Settrade, del tipo de cuenta y de sus autorizaciones.
