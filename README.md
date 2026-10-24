# WallNut (ウォールナット)

全身ポーズアクションゲーム（もじもじくん風）。  
迫りくる壁の穴の形に合わせて全身でポーズをとり、通り抜けるプロジェクター大画面ゲームです。  
VRゴーグルは不使用で、SlimeVRトラッカー（計7点: 胸1, 両手2, 両膝2, 両足首2）と連携してリアルタイムにポーズ判定を行います。

---

## 主な機能・特徴
- **全身7ポーズ完全対応**: 棒立ち(STAND), バンザイ(BANZAI), しゃがみ(SQUAT), Tポーズ(T-POSE), 右腕上げ(RIGHT ARM UP), 左腕上げ(LEFT ARM UP), 片足立ち(FLAMINGO)。
- **SlimeVR リアルタイムOSC連携**: OSC Bundle (`#bundle`) および VRChat OSCトラッカー形式に完全対応。
- **ワンタッチ姿勢・身長キャリブレーション**: 直立姿勢で `[C]` キーを押すだけで、プレイヤーの体格・トラッカー初期角度を自動補正。
- **プロジェクター大画面最適化**: 1920x1080解像度UI、見下ろし固定カメラ、三人称視点。
- **難易度選択＆スコアシステム**: EASY, NORMAL, HARD、コンボボーナス、リザルトランク評価（S+〜C）。

---

## セットアップ手順・マニュアル
詳細な環境構築、Unityシーンのオブジェクト設置、SlimeVRの設定手順、プレイ方法については以下を参照してください：

📖 **[セットアップ＆運用手順書 (docs/setup_guide.md)](docs/setup_guide.md)**

---

## クイックスタート
1. **SlimeVR Server**:
   - 「設定」→「OSC」→「VRChat OSCトラッカー」を **有効**、ポートアウト **`9000`**、アドレス **`127.0.0.1`**。
   - ※「OSCルーター」は必ず **無効 (OFF)**。
2. **Unity**:
   - Unity 2022.3 でプロジェクトを開く。
   - `Assets/Scenes/SampleScene.unity` を開く。
   - 上部メニュー **Tools** から一発生成ツールを実行：
     1. `Tools` ➡ `Build All Wall Prefabs`
     2. `Tools` ➡ `Build Dummy Character`
     3. `Tools` ➡ `Build Projector UI`
     4. `Tools` ➡ `Setup Optimal Camera`
   - Play（再生）ボタンを押す。
   - まっすぐ立って **`[C]` キー** を押してキャリブレーション完了！
