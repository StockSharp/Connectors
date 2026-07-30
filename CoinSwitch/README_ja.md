# CoinSwitch コネクター
[English](README.md) | [Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md)

**CoinSwitch コネクター**は、StockSharp を CoinSwitch PRO API に接続します。商品設定により、INR または USDT 現物市場、USDT 証拠金無期限先物、プライベートベータ版 HFT オプションを選択します。

## 主な機能

- 選択した CoinSwitch 商品の銘柄検索。
- Level 1、板情報、ティック約定、時間枠別ローソク足の購読。
- 商品と時間枠が対応する場合、履歴取得後に WebSocket でライブ更新。
- 現物の指値、先物の指値・成行・ストップ成行、HFT オプションの指値・成行注文を送信。
- 対応するデリバティブ注文での Reduce-only と、HFT オプションでの有効期間指定。
- 個別注文または条件に一致する注文群の取消。
- 残高、ポジション、現在・過去の注文、自身の約定の取得。

## 主な用途

選択した一つの商品領域での CoinSwitch PRO 市場監視と自動売買に適しています。プライベート操作には適切な権限を持つ API キーと Ed25519 シークレットが必要で、オプションには CoinSwitch HFT プライベートベータへのアクセスも必要です。

機能は商品ごとに異なります。現物は指値注文のみ、条件付き注文は先物のストップ成行のみで、オプションのローソク足は WebSocket 配信されません。アトミックな注文変更、アイスバーグ、GTD、板の差分更新、注文ログには対応しません。CoinSwitch の権限とレート制限が適用されます。
