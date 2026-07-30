# Chainflip-Konnektor
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Português](README_pt.md) | [日本語](README_ja.md)

Der **Chainflip-Konnektor** verbindet StockSharp mit dem chainübergreifenden Liquiditätsnetzwerk Chainflip. Er kombiniert öffentliche Daten der State Chain und des Swap-Dienstes mit einer optionalen Wallet-Konfiguration, um Cross-Chain-Swaps über das Transaktionsmodell von StockSharp einzureichen.

## Wichtige Funktionen

- Unterstützte Chainflip-Pools und -Assets suchen.
- Level-1-Werte, Pooltiefe und aus Poolzustand und Ausführungen abgeleitete Trades empfangen.
- Endpunkte für State Chain, Quote-Dienst, Ethereum und Arbitrum konfigurieren.
- Eine Quote anfordern und eine Market-Order als geschützten Cross-Chain-Swap senden.
- Eingereichte Swaps verfolgen und Wallet-Salden als Portfolionachrichten bereitstellen.
- Zieladressen für Assets auf den unterstützten Chains konfigurieren.

## Typische Verwendung

Der Konnektor eignet sich zur Überwachung der Chainflip-Liquidität oder zur sofortigen Ausführung von Cross-Chain-Swaps aus einer konfigurierten Wallet. Öffentliche Marktdaten benötigen keinen Signierschlüssel; zur Ausführung sind Wallet-Adresse, privater Schlüssel, Zieladressen und erreichbare Chain-Endpunkte erforderlich.

Dies ist eine Protokollintegration und keine Order-Schnittstelle einer zentralen Börse. Der Adapter bietet keine Kerzen, Limit-, bedingten oder ruhenden Orders. Nach der Übertragung kann ein Swap weder storniert noch ersetzt oder gesammelt storniert werden. Netzwerkgebühren, Finalität, Liquidität, Slippage und Chain-Verfügbarkeit beeinflussen die Ausführung.
