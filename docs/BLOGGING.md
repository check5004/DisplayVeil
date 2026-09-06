# 紹介記事と画像の使い方

## Qiita 用原稿

[qiita-display-veil.txt](blog/qiita-display-veil.txt) は UTF-8 のテキストファイルです。
冒頭の HTML コメントにタイトル案とタグ候補を記載しています。本文はそのコメントの後から貼り付けます。
見出し・表・参照形式の画像・コードブロックに加え、Qiita の `:::note info` と折りたたみ記法を使用しています。

## 画像素材

| 用途 | SVG 原本 | 掲載用 PNG | PNG のサイズ |
|---|---|---|---|
| 概要・使用前後の比較 | [display-veil-overview.svg](images/display-veil-overview.svg) | [display-veil-overview.png](images/display-veil-overview.png) | 2880 × 1600 |
| 一時操作・自動復帰の説明 | [display-veil-controls.svg](images/display-veil-controls.svg) | [display-veil-controls.png](images/display-veil-controls.png) | 2880 × 1640 |
| 実際の設定画面 | — | [DisplayVeil_SampleImage.png](images/DisplayVeil_SampleImage.png) | 902 × 825 |

SVG はアプリのダークグレーとミントグリーンに合わせた手書きのベクター図解です。図解内に「動作のイメージ」と記載し、実際のスクリーンショットと区別しています。
画像生成 AI・外部画像・外部フォントの読み込み・スクリプトは使用していません。
スクリーンショットは提供されたファイルをそのまま使っています。

SVG の文字は編集可能なテキストで、Yu Gothic / Meiryo / Noto Sans JP / sans-serif の順で指定しています。閲覧環境によってフォントが変わるため、ブログでは描画確認済みの PNG を使うと見た目が揃います。図解を編集したときは、同名の PNG も SVG の2倍の寸法で書き出してください。

## 掲載手順

1. GitHub Releases に配布 ZIP が公開され、ログインしていない読者もダウンロードできることを確認します。記事の配布先は、このリポジトリの `origin` に合わせています。
2. Qiita のタイトル欄にタイトル案を入れ、本文を貼り付けます。タグは候補から選びます。
3. 上の PNG 3枚を Qiita のエディタへアップロードします。返された URL を原稿末尾の `[overview]:`、`[screenshot]:`、`[controls]:` の URL と差し替えます。アップロードで本文中に自動挿入された画像記法は、重複しないよう整理します。
4. プレビューで画像3枚、表、ノート、折りたたみ、ダウンロード先を確認してから公開します。

原稿には代替として、このリポジトリの `main` にある画像を参照する GitHub Raw URL を設定しています。そのまま使う場合は、画像を含む変更が公開され、各 URL が読者から開けることを掲載前に確認してください。

Qiita のアップロード対応形式には SVG が含まれていないため、アップロードには PNG を使用します。[Qiita 公式：画像のアップロード・削除方法](https://help.qiita.com/ja/articles/qiita-image-upload)
記法は [Qiita 公式：Markdown記法 チートシート](https://qiita.com/Qiita/items/c686397e4a0f4f11683d) を参照しています。

GitHubのRelease公開とは別に、Qiitaへの投稿・画像アップロードは掲載者が行います。

## 他のブログへの転用

PNG と本文を利用できます。Qiita 固有の `:::note info` は通常の段落や引用へ置き換え、画像 URL は掲載先に合わせてください。
ソースコード・ドキュメント・図解・アプリアイコンは [MIT License](../LICENSE) で提供します。著作権表示とライセンス全文を残して利用してください。
