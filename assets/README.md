# Display Veil アプリアイコン

暗い左右の画面と、ミントグリーンの鑑賞画面を重ねたデザインです。中央の再生マークで映画・動画の用途を示します。小さなタイトルバーでも見分けられるよう、文字・細かい模様・グラデーションは使っていません。

- `DisplayVeil.svg`：編集用の原本。256 × 256、外周は透過。
- `DisplayVeil.png`：確認・紹介用の512 × 512 PNG。
- `DisplayVeil.ico`：Windows用。16 / 20 / 24 / 32 / 40 / 48 / 64 / 96 / 128 / 256pxの10サイズ。

ICOをEXEのネイティブアイコンとマネージドリソースの両方に埋め込みます。タイトルバー・タスクバー・通知領域も同じデザインです。配布先で外部のICOファイルを置く必要はありません。

## SVGを編集した後の再生成

通常のビルドはコミット済みのICOを使用し、Node.jsや追加パッケージは不要です。アイコンの再生成だけ、Node.jsと`sharp`を使用します。

リポジトリ直下からPowerShellで実行します。

```powershell
npm install --no-save --package-lock=false --prefix artifacts/icon-tools sharp
$env:NODE_PATH = "$PWD/artifacts/icon-tools/node_modules"
node tools/generate-app-icon.cjs
```

16〜128pxはWinForms用の32bit DIB、256pxはExplorerの大きいアイコン表示用のPNG形式でICOに格納します。SVG・PNG・ICOをセットでコミットしてください。
