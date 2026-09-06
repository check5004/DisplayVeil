# GitHub Actions と配布

## 自動実行

`.github/workflows/windows.yml` は GitHub の Windows 2022 ランナーを使用します。

| きっかけ | 処理 |
|---|---|
| ブランチへの push / pull request | exe ビルド、画面を表示しないテスト、配布ZIPとSHA-256の生成、配布処理のローカル検証、Actions Artifact 保存 |
| Actions の Run workflow | 同じ検証と Artifact 保存。Release は公開しない |
| `v1.0.0` 形式のタグを push | 上記すべての成功後、GitHub Releases にZIPとSHA-256を公開 |

Artifact は14日間保持します。Release はタグの push 時だけ作成します。
`vMAJOR.MINOR.PATCH` の正式版タグを受け付け、先頭ゼロ・プレリリース接尾辞のあるタグは拒否します。
署名用証明書や追加PATは不要です。公開ジョブにだけ `contents: write` を付与し、標準の `GITHUB_TOKEN` を使用します。
このフローでは exe にコード署名を行いません。

## 最初の公開

1. GitHub にリポジトリを作成し、このローカルリポジトリの `origin` にそのURLを登録します。
2. `main` を push し、Actions の `Windows build and release` が成功することを確認します。
3. 次のコマンドで公開用タグを push します。

```powershell
git push -u origin main
git tag -a v1.0.0 -m "Display Veil 1.0.0"
git push origin v1.0.0
```

タグは `.github/workflows/windows.yml` を含むコミットに付けてください。
リポジトリ・OrganizationのポリシーでActionsや書き込み権限が制限されている場合は、その設定に従う必要があります。
privateリポジトリのReleaseもprivateのままです。誰でも取得できる配布先にする場合は、公開リポジトリを使用してください。

## 次のバージョン

`src/AssemblyInfo.cs` の `AssemblyVersion` と `AssemblyFileVersion` を同じ値へ更新します。
例えば1.0.1は両方 `1.0.1.0` とし、対応するタグは `v1.0.1` です。
exeのバージョンとタグが一致しない場合は、公開前のビルドで失敗します。ZIPの名前はexeのバージョンから生成します。

```powershell
.\build.ps1 -Test -Package -ExpectedVersion 1.0.1
git add src/AssemblyInfo.cs
git commit -m "chore: release version 1.0.1"
git push origin main
git tag -a v1.0.1 -m "Display Veil 1.0.1"
git push origin v1.0.1
```

## 生成物と配布方法

ローカルの生成先は `artifacts/release/` です。Release の Assets に次のファイルを公開します。

- `DisplayVeil-1.0.0-win.zip`：exe、exe.config、MITライセンス全文（`LICENSE`）、README、画像・ドキュメント一式
- `DisplayVeil-1.0.0-win.zip.sha256`：ZIPのSHA-256

利用者はZIPを展開し、`DisplayVeil/DisplayVeil.exe` を起動します。
必要な `DisplayVeil.exe.config` を一緒に届けるため、exeを含むZIPを配布単位にしています。
GitHubが自動生成する `Source code (zip)` はソースだけで、ビルド済みexeを含みません。

確認例:

```powershell
Get-FileHash .\DisplayVeil-1.0.0-win.zip -Algorithm SHA256
Get-Content .\DisplayVeil-1.0.0-win.zip.sha256
```

## 失敗と再実行

公開スクリプトはZIPのSHA-256を確認し、Draft Releaseを作成して、両ファイルのアップロード成功後に公開します。
アップロードに失敗した場合はDraftのまま残し、失敗ジョブを再実行するとDraftへのアップロードから再開します。
公開済みの同じタグのReleaseは再実行でも書き換えません。差し替えが必要な修正は新しいバージョン・タグで公開します。

タグはリモートに存在するものを使います。スクリプト自身が別コミットへタグを自動作成することはありません。
GitHub CLI の [`gh release create --verify-tag`](https://cli.github.com/manual/gh_release_create) と、
[`gh release upload`](https://cli.github.com/manual/gh_release_upload) を使用しています。

## テストとローカル検証

```powershell
# 実画面上のテストを含む既存の検証
.\build.ps1 -Test -Package

# GitHub Actions と同じ、画面を表示しない検証
.\build.ps1 -Test -Headless -Package -ExpectedVersion 1.1.0
.\tests\ReleasePipeline.Tests.ps1
```

Headless モードはロジック・設定・アイコン・更新確認の39件を実行し、実デスクトップを必要とする9件を `SKIP` と明示します。
CIで実際の複数画面・マウス操作を確認したことにはなりません。公開前の実機チェックは `TESTING.md` を使用してください。
配布処理のテストはGitHub CLIをモックし、ZIP内容・ハッシュ・バージョン不一致・Draft再開・アップロード失敗などを確認します。
テスト自体がGitHubへ通信したりReleaseを作成したりすることはありません。

Actionsは公式リポジトリで確認したコミットSHAに固定しています。更新する場合は各Actionの公式リリースを確認してSHAを更新してください。

## 更新通知の公開後テスト

v1.0.0の公開済みバイナリには更新確認機能がないため、今回のソースからバージョンだけを下げた検証用exeを作る。

```powershell
.\tools\New-UpdateTestBuild.ps1 -Version 1.0.0
.\artifacts\update-check-test\DisplayVeil.exe --settings-dir .\artifacts\update-check-test\settings
```

検証用exeは `artifacts/update-check-test/` に隔離し、配布ZIPやGitには含めない。通常の設定も変更しない。アプリは二重起動を防ぐため、別のDisplay Veilは先に終了する。

1. v1.1.0公開前に検証用1.0.0を起動し、公開中のv1.0.0に対して更新なしになることを確認する。
2. 実装・ローカルテストを完了し、v1.1.0タグをpushする。今回はここで作業を停止し、利用者がActionsを監視する。
3. 公開完了の連絡後、検証用1.0.0の「今すぐ更新を確認」でv1.1.0の通知を確認する。開いたままでは自動検出しない。
4. 通知リンクがv1.1.0のページを開き、配布ZIPが取得できることを確認する。
5. 検証用exeを閉じ、別の空の `--settings-dir` で再起動し、初回起動の自動確認でもv1.1.0を検知することを確認する。通常は24時間制限があるので単なる再起動では再通信しない。
6. 公開ZIPを別フォルダーへ展開し、同じ検証用設定で新版を起動する。設定が保持され、手動確認で更新なしになることを確認する。

公開後の実行結果は `TESTING.md` に追記する。
