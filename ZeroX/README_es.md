# Conector de 0x
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de 0x** conecta StockSharp con 0x Swap API v2 y redes EVM compatibles. Representa pares de tokens configurados como instrumentos y convierte precios ejecutables, saldos y swaps enrutados en mensajes de StockSharp.

## Funciones principales

- Descubrimiento de pares configurados en Ethereum, Optimism, BNB Chain, Polygon, Base, Arbitrum, Avalanche y Linea.
- Precios bid y ask de nivel 1 obtenidos por sondeo de precios ejecutables de 0x.
- Obtención, firma y difusión de swaps de mercado inmediatos mediante el JSON-RPC de la red seleccionada.
- Aprobación automática opcional de allowances y configuración de deslizamiento, volumen de prueba y espera de recibos.
- Saldos de tokens y seguimiento de recibos, estados de órdenes y ejecuciones.
- Clave de 0x Dashboard, cartera, pares y endpoints API y RPC configurables.
- No ofrece ticks, libros, velas, historial, órdenes en espera, sustitución ni cancelación.
- Las rutas, unidades, aprobaciones, firma y recibos EVM quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisar precios ejecutables de tokens, paneles de cartera y ejecutar directamente swaps enrutados por 0x en una red EVM compatible.

La cobertura, las rutas, la liquidez, el impacto, el gas, las aprobaciones, la finalidad y los límites dependen de 0x, la red y el proveedor RPC.
