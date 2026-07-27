# Dukascopy JForex コネクター

[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**Dukascopy JForex コネクター**は、公式 Java JForex SDK を使用して StockSharp を Dukascopy Bank に接続します。Dukascopy 取引サーバーとの安全な認証済みセッションは SDK が確立し、.NET アダプターはローカル専用ブリッジを介してコマンドとイベントを交換します。

## 主な機能

- 口座で利用できる FX、CFD、金属、指数、商品、債券などの銘柄検索。
- Level 1 気配値、ティック約定、板更新、時間足。
- JForex 履歴サービスによる過去ティックとローソク足。
- 成行、指値、ストップ、ストップリミット、および JForex 固有の注文命令。
- 注文の登録、変更、取消、約定、残高、ポジション更新。
- デモ用と本番用に分離された設定可能な JForex サービスアドレス。
- 指定した実行可能 JAR からのブリッジ起動、または独立したローカルプロセスとしての運用。

## 実行モデル

Dukascopy は JForex を Java API として公開・サポートしているため、Java が必要です。同梱の Maven ブリッジプロジェクトは公式 `DDS2-jClient-JForex` パッケージを使用します。ブリッジはローカルのループバックインターフェースだけで待ち受け、口座認証情報をネットワークへ公開しません。

標準 StockSharp メッセージモデルで Dukascopy を利用する売買ロボット、端末、監視、注文管理に適しています。銘柄、履歴、板の深さ、取引権限は Dukascopy 口座に依存します。
