# Conector de Velora
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Velora** conecta StockSharp con Velora Market API y redes EVM compatibles. Representa pares de tokens configurados como instrumentos y convierte precios ejecutables, saldos y swaps enrutados en mensajes de StockSharp.

## Funciones principales

- Descubrimiento de pares configurados en Ethereum, Optimism, BNB Chain, Gnosis, Polygon, Base, Arbitrum y Avalanche.
- Precios bid y ask de nivel 1 obtenidos por sondeo de rutas ejecutables de Velora.
- Construcción, firma y difusión de swaps de mercado inmediatos mediante el JSON-RPC de la red seleccionada.
- Aprobación automática opcional y configuración de deslizamiento, volumen de prueba y espera de recibos.
- Saldos de tokens y seguimiento de recibos, estados de órdenes y ejecuciones.
- Identificador de socio Velora, cartera, pares y endpoints API y RPC configurables.
- No ofrece ticks, libros, velas, historial, órdenes en espera, sustitución ni cancelación.
- Las rutas, unidades, aprobaciones, firma y recibos EVM quedan ocultos tras la API estándar de StockSharp.

## Uso habitual

Úselo para supervisar cotizaciones entre tokens, paneles de cartera y ejecutar directamente swaps enrutados por Velora en una red EVM compatible.

La cobertura, las rutas, la liquidez, el impacto, el gas, las aprobaciones, la finalidad y los límites dependen de Velora, la red y el proveedor RPC.
