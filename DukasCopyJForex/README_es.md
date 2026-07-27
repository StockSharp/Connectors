# Conector de Dukascopy JForex

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Dukascopy JForex** enlaza StockSharp con Dukascopy Bank mediante el SDK oficial de JForex para Java. El SDK establece la sesión segura y autenticada con los servidores de negociación; el adaptador .NET intercambia órdenes y eventos con él mediante un puente exclusivamente local.

## Funciones principales

- Consulta de instrumentos de FX, CFD, metales, índices, materias primas y bonos disponibles para la cuenta.
- Cotizaciones de nivel 1, operaciones tick, cambios del libro y velas temporales.
- Ticks y velas históricos mediante los servicios de historial de JForex.
- Órdenes de mercado, límite, stop, stop-limit y comandos específicos de JForex.
- Alta, modificación y cancelación de órdenes, ejecuciones, saldos y posiciones.
- Direcciones JForex separadas y configurables para los entornos demo y real.
- Inicio del puente desde un JAR ejecutable indicado o uso como proceso local independiente.

## Modelo de ejecución

Se requiere Java porque Dukascopy publica y mantiene JForex como API Java. El proyecto Maven incluido utiliza el paquete oficial `DDS2-jClient-JForex`. El puente escucha únicamente en la interfaz loopback y no expone las credenciales de la cuenta a la red.

El conector sirve para robots, terminales, supervisión y gestión de órdenes con el modelo estándar de mensajes StockSharp. Los instrumentos, el historial, la profundidad y los permisos dependen de la cuenta Dukascopy.
