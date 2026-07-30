# Conector de XRPL
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de XRPL** conecta StockSharp con el exchange descentralizado integrado en XRP Ledger. Convierte pares configurados, libros del ledger, ofertas ejecutadas, saldos y transacciones firmadas en mensajes de StockSharp.

## Funciones principales

- Descubrimiento de pares configurados de XRP y tokens emitidos, con selección opcional de dominio DEX autorizado.
- Nivel 1 y libros de profundidad configurable con actualizaciones continuas del ledger.
- Operaciones tick a tick históricas y en vivo derivadas de cambios del libro, con velas construidas a partir de la actividad del ledger.
- Ofertas limitadas e IOC de mercado protegidas por precio, más cancelación, sustitución y cancelación grupal supervisada.
- Saldos, ofertas abiertas, estados, ejecuciones, comisiones y estado de transacciones.
- Los datos públicos solo requieren RPC y WebSocket; para negociar se necesitan dirección clásica y family seed.
- El historial está limitado por el máximo de ledgers y las instantáneas usan el intervalo de sondeo configurado.
- Los importes, emisores, firmas, secuencias, comisiones y eventos XRPL quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para terminales DEX de XRPL, análisis del ledger, estudios históricos, supervisión de cuentas y ejecución directa de ofertas.

La cobertura, la liquidez, el historial, los costes, la finalidad, los dominios autorizados y los endpoints dependen del estado de XRPL y del servicio configurado.
