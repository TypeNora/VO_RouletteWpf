# VO Roulette WPF

## Status

- このプロジェクトが現行の本体実装です。
- `VO_roulette_app` は過去のElectron試作版としてアーカイブ運用します。

## 方針

- Step 1: 既存Webコンテンツを `WebView2` で内包
- Step 2: UI/ロジックを純WPFへ移植

現状は Step 2 に移行済みで、起動時UIは純WPFです。

## 構成

- `src/VoRoulette.Wpf`: 純WPFアプリ（ルーレット描画・開始停止・編集UI）
- `src/VoRoulette.Core`: 抽選・プリセット・状態保存

## Windowsでの実行

前提:
- Visual Studio 2022
- .NET 8 SDK

手順:
1. `VO_roulette_wpf/VO_roulette_wpf.sln` を Visual Studio で開く
2. スタートアッププロジェクトを `VoRoulette.Wpf` に設定
3. 実行

## Release出力（VS Code GUI）

1. `Terminal` > `Run Task...`
2. 次のどちらかを選択
   - `publish VoRoulette.Wpf (Release, win-x64)`  
   - `publish VoRoulette.Wpf (Release, win-x64, self-contained)`
3. 出力先
   - `VO_roulette_wpf/artifacts/publish/win-x64`
   - `VO_roulette_wpf/artifacts/publish/win-x64-self-contained`

## 実行ファイルをGitHubで管理する

配布用は `exe + assets` を1つのzipにして管理します。
生成物:

- `VO_roulette_wpf/artifacts/releases/VO_RouletteWpf-win-x64.zip`

PowerShell:

```bash
pwsh -File VO_roulette_wpf/scripts/package-win-x64.ps1
git -C VO_roulette_wpf add artifacts/releases/VO_RouletteWpf-win-x64.zip
git -C VO_roulette_wpf commit -m "chore: add win-x64 release zip"
git -C VO_roulette_wpf push
```

zipの中身:

- `VoRoulette.Wpf.exe`（self-contained single-file）
- `assets/`（そのまま同梱）

## 実装済み（純WPF）

1. キャラのON/OFF・名称・重み編集
2. プリセット切替
3. お気に入り3枠の保存/読込
4. ルーレット描画と開始/停止アニメーション
5. `AppData` 配下へのJSON保存
6. `アーケード選択モード` タブ（左右バナー走査ランダム）

## 作成サンプル画像

以下は本アプリでの表示例を示す**作成サンプル画像**です（実機スクリーンショット）。

- DNA表示サンプル  
  ![作成サンプル画像: DNA表示](docs/images/voot-dna-sample.png)
- RNA表示サンプル  
  ![作成サンプル画像: RNA表示](docs/images/voot-rna-sample.png)

## アーケードバナー画像

- 配置先: `src/VoRoulette.Wpf/assets/arcade-banners`
- 参照先ディレクトリ:
  - `source/OMG`
  - `source/VOOT`
  - `source/FORCE`
  - `source/VOINDEX`
- `VOOT` は `DNA/RNA` 切り替え対応
  - ファイル名先頭に `DNA_` または `RNA_` を付ける
  - 例: `DNA_RAIDEN.png`, `RNA_BALBADOS.png`
