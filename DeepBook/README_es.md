# Conector de DeepBook
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

El **conector de DeepBook** integra StockSharp con el protocolo de liquidez DeepBook de Sui. Combina el indexador público de DeepBook con un nodo completo Sui por gRPC para datos de pools, saldos de cartera y swaps inmediatos firmados localmente.

## Funciones principales

- Descubrir pools de DeepBook y filtrarlos opcionalmente por nombre, identificador o código de instrumento.
- Solicitar instantáneas de Level 1, profundidad y operaciones tick históricas o actualizadas por sondeo.
- Descargar y actualizar mediante sondeo velas desde 1 minuto hasta 7 días.
- Configurar indexador, nodo Sui, paquete, objeto de reloj, profundidad, historial e intervalo de consulta.
- Exponer saldos de tokens Sui como cartera de StockSharp al configurar una dirección.
- Enviar una orden de mercado como swap DeepBook firmado localmente y protegido frente al deslizamiento.
- Seguir el resumen de la transacción Sui y la ejecución del swap.

## Uso habitual

Use este conector para vigilar pools de DeepBook, recopilar datos de la DEX de Sui o ejecutar swaps inmediatos desde una cartera configurada. Los datos públicos no necesitan clave privada; la cartera requiere una dirección y la ejecución necesita su clave Ed25519.

La interfaz transaccional representa swaps inmediatos, no órdenes pendientes de DeepBook. No hay órdenes limitadas, condicionales, post-only ni vigencia, y una transacción Sui ejecutada no puede cancelarse, sustituirse ni cancelarse en grupo. La latencia, cobertura del indexador, deslizamiento, gas, liquidez y finalidad de Sui afectan al resultado.
