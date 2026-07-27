# Conector de Bit2Me
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Bit2Me** conecta StockSharp con Bit2Me Pro, la plataforma de negociación al contado del proveedor español de activos digitales. Resulta útil para sistemas que necesitan acceso directo a mercados de criptomonedas con liquidez en EUR mediante el modelo estándar de mensajes de StockSharp.

## Funciones principales

- Descubrimiento de mercados al contado de Bit2Me Pro y de sus reglas de precio, cantidad y orden mínima.
- Instantáneas REST de cotizaciones de nivel 1 y del libro de órdenes de nivel 2.
- Operaciones públicas y actualizaciones completas del libro en tiempo real mediante WebSocket.
- Operaciones históricas y velas OHLCV para los intervalos publicados por Bit2Me.
- Envío de órdenes de mercado, limitadas y stop-limit.
- Cancelación y consulta de órdenes y ejecuciones.
- Saldos de cartera y fondos bloqueados por órdenes activas.
- Direcciones REST y WebSocket configurables para pruebas, enrutamiento o cambios de infraestructura.

## Uso habitual

Utilice el conector en robots de trading, terminales, recolectores de datos, servicios de gestión de órdenes y herramientas de supervisión para instrumentos al contado de Bit2Me Pro.

Los datos públicos de mercado no requieren credenciales. Las operaciones de trading y cuenta requieren una clave API y un secreto de Bit2Me con los permisos adecuados. Bit2Me controla los mercados, límites y funciones disponibles para la cuenta.
