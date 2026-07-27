# Conector BtcTurk

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector BtcTurk** conecta StockSharp con BtcTurk Kripto, una bolsa turca de criptomonedas al contado. Está pensado para sistemas de negociación y datos de mercado que operan con mercados en TRY, BTC, USDT y otras divisas mediante el modelo de mensajes estándar de StockSharp.

## Funciones principales

- Consulta de instrumentos al contado y de sus límites de precio, volumen y órdenes.
- Cotizaciones de nivel 1, instantáneas del libro de nivel 2 y operaciones públicas.
- Tickers, libros de órdenes y operaciones en tiempo real mediante WebSocket.
- Velas históricas OHLCV para los intervalos admitidos por BtcTurk.
- Saldos de cartera, órdenes abiertas e históricas y operaciones de la cuenta.
- Envío de órdenes de mercado, límite, stop de mercado y stop límite.
- Cancelación de una orden o de un grupo de órdenes.
- Direcciones REST, de históricos y WebSocket configurables.

## Uso habitual

Utilice el conector en robots de trading, terminales, recopiladores de datos y sistemas de gestión de órdenes o supervisión para los mercados al contado de BtcTurk Kripto.

Los datos públicos no requieren credenciales. Para negociar y acceder a la cuenta se necesitan una clave API de BtcTurk y un secreto codificado en Base64 con los permisos adecuados. En una compra a mercado, BtcTurk interpreta la cantidad como un importe en la divisa cotizada; en las demás órdenes se expresa en el activo base.
