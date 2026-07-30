# Conector de Chainflip
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de Chainflip** integra StockSharp con la red de liquidez entre cadenas Chainflip. Combina datos públicos de State Chain y del servicio de intercambio con una configuración opcional de cartera para enviar swaps entre cadenas mediante el modelo transaccional de StockSharp.

## Funciones principales

- Descubrir los pools y activos compatibles con Chainflip.
- Recibir Level 1, profundidad de los pools y operaciones derivadas del estado y las ejecuciones.
- Configurar los endpoints de State Chain, cotizaciones, Ethereum y Arbitrum.
- Solicitar una cotización y enviar una orden de mercado como swap entre cadenas protegido.
- Seguir los swaps enviados y exponer los saldos de la cartera mediante mensajes de portafolio.
- Configurar direcciones de destino para activos de las cadenas compatibles.

## Uso habitual

Use este conector para vigilar la liquidez de Chainflip o ejecutar swaps inmediatos entre cadenas desde una cartera configurada. Los datos públicos no necesitan una clave de firma; la ejecución requiere dirección de cartera, clave privada, direcciones de destino y endpoints operativos.

Se trata de una integración con un protocolo, no de una interfaz de órdenes de una bolsa centralizada. El adaptador no ofrece velas, órdenes limitadas, condicionales ni órdenes en espera. Una vez difundida la transacción, el swap no puede cancelarse, sustituirse ni cancelarse en grupo. Las comisiones, la finalidad, la liquidez, el deslizamiento y la disponibilidad de las cadenas afectan a la ejecución.
