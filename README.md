# 基于大语言模型的游戏NPC决策与交互系统

---

## 📑 目录 | Table of Contents

### 中文
- [项目背景](#项目背景)
- [项目简介](#项目简介)
- [注意事项](#注意事项)
- [核心功能](#核心功能)
- [目前已实现部分](#目前已实现部分)
- [更新日志](#更新日志--update-log)
- [鸣谢与资源引用](#鸣谢与资源引用)
- [免责声明](#免责声明)

### 日本語
- [プロジェクト背景](#プロジェクト背景)
- [プロジェクト概要](#プロジェクト概要)
- [注意事項](#注意事項)
- [主な機能](#主な機能)
- [実装済み機能](#実装済み機能)
- [更新履歴](#更新履歴--update-log)
- [謝辞・外部リソース](#謝辞外部リソース)
- [免責事項](#免責事項)

---

## 项目背景

本项目为河南农业大学计算机科学与技术（软件技术方向）专业某本科学生的毕业设计课题。项目围绕大语言模型（LLM）在游戏NPC智能决策与交互系统中的应用展开，旨在通过系统构建与实验验证，探索其在动态叙事生成、情境感知交互以及游戏智能体行为生成方面的可行性与实现路径。

---

## 项目简介

本课题聚焦于传统随机生成型地牢游戏中 NPC 依赖预设脚本、交互形式单一、行为固化以及缺乏动态叙事能力等问题。为此，引入大语言模型（LLM）作为智能决策引擎，为 NPC 赋予上下文感知的自然语言对话能力与动态行为生成能力。在 Unity 环境中构建 LLM 集成系统，并为关键 NPC 实现情境依赖型交互机制，从而验证该方法在提升叙事深度、交互多样性以及玩家沉浸感方面的有效性。

---

## 注意事项

### Unity 版本
- Unity 2021.3.32f1c1

---

### DeepSeek API Key 设置方式

本项目通过环境变量 `DEEPSEEK_API_KEY` 读取 API Key。  
请在 Windows PowerShell 中执行以下命令：  

```powershell
setx DEEPSEEK_API_KEY "your_api_key_here"
```

---

## 核心功能

### 一、LLM 驱动的 NPC 决策与交互核心模块

1. 角色敏感提示模板设计  
2. 结构化决策输出与容错机制  
3. 上下文闭环集成  
4. 对话反馈与事件驱动机制  

---

### 二、随机地牢生成模块

1. 基于 BFS 算法的地牢生成  
2. 房间类型智能分配  
3. 地牢状态实时管理  

---

### 三、基础游戏与交互支撑模块

1. 玩家交互控制  
2. 战斗与事件响应  
3. 编辑器与可视化工具  
4. 系统集成与优化  

---

## 目前已实现部分

1. 地牢生成与房间分配  
   - 开始游戏后自动生成一次地牢  
   - 地牢设定最远两个房间为出生点与 Boss 房  
   - 可通过 RoomsFirstDungeonGenerator 按钮测试地牢生成  

2. 玩家控制与战斗流程  
   - WASD 控制移动  
   - 进入战斗房间后自动进入战斗状态，清空当前房间怪物后自动退出战斗  
   - Boss 房通关后延迟触发下一层流程（对话 → 新层生成）  

3. 武器系统与基础射击  
   - 完成基础武器系统架构（BaseWeapon + WeaponManager）  
   - 实现基础手枪与子弹逻辑  

4. 怪物 AI 与死亡处理  
   - 完成简易怪物 AI 系统（闲置游走、玩家追击、攻击判定）  
   - 新增死亡状态标记，防止重复触发逻辑  

5. 游戏生命周期与 UI 管理  
   - 新增玩家死亡判定  
   - 实现游戏暂停逻辑  
   - 实现死亡 UI 触发与重开流程  

6. 事件系统与战斗数值联动（LLM 底层支撑）  
   - 构建 LayerEventSystem，区分单层事件与即时永久事件  
   - 构建战斗倍率系统（玩家/敌人伤害、攻速、移速、视野等）  
   - 完成完整事件生命周期闭环：  
     UI / JSON → LayerEventSystem → Commit → Apply → FloorEnd  
   - 多轮连续通关测试验证生命周期稳定  

7. LLM 接入与 NPC 对话闭环系统  
   - 建立 LLM 调用抽象层（ILLMClient + DeepSeekLLMProvider）  
   - 使用 json_object 输出模式保证结构化响应  
   - 构建 LLMOrchestrator 业务编排层（Decision JSON + Opening Line 分离）  
   - 实现 NPCDecisionUI 状态机（OpeningWaiting / Typing / WaitingLLM / Result）  
   - 开局首句固定，后续每次打开对话 UI 时向 LLM 请求开场白  
   - 玩家发送后请求决策 JSON，点击继续后应用事件与好感度并进入下一层  

8. 本局人格系统（Run-Level Personality）  
   - 使用 ScriptableObject 定义 NPCPersonalityDefinition  
   - 开局随机抽取一次人格，整局固定（DontDestroyOnLoad）  
   - 支持 Prompt 注入（system / opening / decision 级别扩展）  
   - 预留人格 × 好感度区间立绘切换接口

---

## 更新日志 | Update Log

- 26/2/22：完成核心地牢生成、房间类型分配、战斗状态基础逻辑。
- 26/2/23：重构战斗退出逻辑（由ESC手动退出改为清怪自动退出）；实现房间怪物实时检测机制；完成基础武器系统架构（BaseWeapon + WeaponManager）；实现基础手枪与子弹系统；新增 Boss 房通关后延迟自动重生地牢功能。
- 26/2/24：实现完整怪物AI逻辑（闲置游走、追击、攻击、分离）；完善游戏生命周期（玩家死亡判定、暂停、死亡UI）；新增LLM接入底层核心系统（CombatModifierSystem/LayerEventSystem等），区分临时/长期事件类型，完成事件与战斗数值的联动映射，为LLM接入奠定架构基础。
- 26/2/25：完成全部事件接口构建与 JSON 模拟 LLM 输入系统；实现三层事件结构；完成多层连续测试。
- 26/2/27：完成 LLM 真实联网接入（DeepSeek JSON Object 输出）与调用抽象层（ApiKeyProvider/ILLMClient/Provider）；实现 LLMOrchestrator 业务编排（Decision JSON + Opening Line）；重构 NPCDecisionUI 状态机（OpeningWaiting/Typing/Waiting/Result），确定“开局固定首句、后续每次打开 UI 请求开场白”的稳定策略；引入本局人格系统（ScriptableObject 池随机抽取，整局固定）并支持 Prompt 注入，预留人格×好感度立绘切换能力。


---

## 鸣谢与资源引用

1. 地牢美术素材  
   https://pixel-poem.itch.io/dungeon-assetpuck  

2. 地牢生成算法参考教程  
   https://github.com/SunnyValleyStudio/Unity_2D_Procedural_Dungoen_Tutorial  

3. 角色美术素材  
   https://brullov.itch.io/generic-char-asset  

---

## 免责声明

1. 本项目仅用于学习、毕业设计与非商业用途。  
2. 外部素材请遵守原作者授权协议。  
3. 基于 Unity 2021.3.32f1c1 开发，其他版本可能存在兼容问题。  
4. 使用者自行承担使用风险。  
5. 二次分发请保留鸣谢信息。  

---

# 大規模言語モデルに基づくゲームNPCの意思決定および対話システム

---

## プロジェクト背景

本プロジェクトは、河南農業大学（中国・河南省）コンピュータサイエンス専攻（ソフトウェア技術方向）に在籍する学士課程学生の卒業研究課題として実施されるものである。大規模言語モデル（LLM）をゲームNPCの意思決定および対話システムへ応用することを目的とし、動的ナラティブ生成、文脈認識型インタラクション、およびゲームエージェント行動生成の実装可能性について体系的に検証する。

---

## プロジェクト概要

本課題は、従来のランダム生成型ダンジョンゲームにおいてNPCが事前定義スクリプトに依存し、インタラクションの単一性や行動の固定化、動的ナラティブの欠如といった課題を抱えている点に着目する。そこで、大規模言語モデル（LLM）を知的意思決定エンジンとして導入し、NPCに文脈認識型の自然言語対話および動的行動生成能力を付与する。Unity環境においてLLM統合システムを構築し、主要NPCに状況依存型インタラクションを実装することで、物語性の深化、対話多様性の向上、およびプレイヤー没入感の強化に対する有効性を検証する。

---

## 注意事項

### Unity バージョン
- Unity 2021.3.32f1c1

---

### DeepSeek API Key 設定方法

本プロジェクトでは環境変数 `DEEPSEEK_API_KEY` から API Key を取得します。  
Windows PowerShell にて以下のコマンドを実行してください：

```powershell
setx DEEPSEEK_API_KEY "your_api_key_here"
```

---

## 主な機能

### LLM駆動NPC意思決定・対話コアモジュール

1. キャラクター特性を考慮したプロンプト設計  
2. 構造化意思決定出力とフォールトトレランス機構  
3. コンテキスト循環統合アーキテクチャ  
4. 対話フィードバックとイベント駆動機構  

---

### ランダムダンジョン生成モジュール

1. BFSアルゴリズムに基づくダンジョン生成  
2. 部屋タイプの動的割り当て  
3. ダンジョン状態管理  

---

### 基礎ゲーム支援モジュール

1. プレイヤー移動制御  
2. バトル状態管理  
3. エディタ拡張および可視化ツール  
4. システム統合・最適化  

---

## 実装済み機能

1. ダンジョン生成および部屋構造  
   - ゲーム開始時に自動でダンジョンを1回生成  
   - 最遠距離の2部屋をスポーン地点とボス部屋に設定  
   - RoomsFirstDungeonGenerator ボタンによりダンジョン生成テストが可能  

2. プレイヤー操作および戦闘フロー  
   - WASD による移動操作  
   - 戦闘部屋に入ると自動で戦闘状態へ移行し、部屋内の敵を全滅させると自動で戦闘終了  
   - ボス部屋クリア後、一定時間後に次階層フロー（対話 → 新階層生成）を開始  

3. 武器システムおよび基本射撃機構  
   - BaseWeapon + WeaponManager による武器管理アーキテクチャを構築  
   - 基本的なハンドガンおよび弾丸システムを実装  

4. 敵AIおよび死亡処理  
   - 簡易敵AIを実装（待機巡回、プレイヤー追跡、攻撃判定）  
   - 死亡状態フラグを追加し、重複処理の発生を防止  

5. ゲームライフサイクルおよびUI管理  
   - プレイヤー死亡判定を実装  
   - ゲーム一時停止ロジックを実装  
   - 死亡UI表示およびリスタート処理を実装  

6. イベントシステムおよび戦闘数値連動（LLM基盤）  
   - LayerEventSystem を構築し、単層イベントと即時永続イベントを区別  
   - 戦闘倍率システムを構築（プレイヤー／敵の攻撃力・攻撃速度・移動速度・視野など）  
   - 完全なイベントライフサイクルを実装：  
     UI / JSON → LayerEventSystem → Commit → Apply → FloorEnd  
   - 複数階層連続テストにより安定性を検証  

7. LLM連携およびNPC対話クローズドループ  
   - LLM呼び出し抽象層を構築（ILLMClient + DeepSeekLLMProvider）  
   - json_object 出力モードを採用し、構造化レスポンスを保証  
   - LLMOrchestrator による業務編成層を実装（Decision JSON と Opening Line を分離）  
   - NPCDecisionUI の状態管理を実装（OpeningWaiting / Typing / WaitingLLM / Result）  
   - ゲーム開始時の最初の発話は固定、それ以降は対話UI表示時にLLMへ開場発話を要求  
   - プレイヤー送信後に Decision JSON を取得し、「続行」操作でイベントと好感度を適用し次階層へ遷移  

8. 本局人格システム（Run-Level Personality）  
   - ScriptableObject により NPCPersonalityDefinition を定義  
   - ゲーム開始時に人格をランダム抽出し、セッション中は固定（DontDestroyOnLoad）  
   - Prompt 注入に対応（system / opening / decision レベル拡張）  
   - 人格 × 好感度区間による立ち絵切替インターフェースを予約実装

---

## 更新履歴 | Update Log

- 26/2/22：コアダンジョン生成、部屋タイプ割当、戦闘状態の基礎ロジックを実装。  
- 26/2/23：戦闘終了ロジックをESC手動解除方式から敵全滅による自動解除方式へ変更；部屋内敵リアルタイム検出機構を実装；基本武器システムアーキテクチャ（BaseWeapon + WeaponManager）を構築；基本ピストルおよび弾丸システムを実装；Bossルーム通関後の遅延ダンジョン自動再生成機能を追加。  
- 26/2/24：敵AIの完全ロジック（待機時遊走、追跡、攻撃、分離）を実装；ゲームライフサイクル（プレイヤー死亡判定、一時停止、死亡UI）を完善；LLM統合の基盤コアシステム（CombatModifierSystem / LayerEventSystem 等）を追加し、一時／長期イベントタイプを区分、イベントと戦闘数値の連動マッピングを完成、LLM統合の基盤を構築。  
- 26/2/25：全イベントインターフェースを構築；JSONによるLLM入力シミュレーションシステムを実装；三層構造イベントシステムを構築；多層連続テストを完了。  
- 26/2/27：DeepSeek による LLM の実ネットワーク接続を完了（json_object 出力モードを使用）。ApiKeyProvider / ILLMClient / DeepSeekLLMProvider による呼び出し抽象層を構築。  
  LLMOrchestrator にて Decision JSON と Opening Line を分離した業務編成ロジックを実装。  
  NPCDecisionUI の状態機械（OpeningWaiting / Typing / WaitingLLM / Result）を再設計し、「初回は固定発話、それ以降は対話UI表示時にLLMへ開場発話を要求する」安定戦略へ移行。  
  本局人格システム（ScriptableObject プールからランダム抽出、セッション中固定）を導入し、Prompt 注入に対応。人格 × 好感度による立ち絵切替インターフェースを予約実装。


---

## 謝辞・外部リソース

1. ダンジョン素材  
   https://pixel-poem.itch.io/dungeon-assetpuck  

2. ダンジョン生成参考  
   https://github.com/SunnyValleyStudio/Unity_2D_Procedural_Dungoen_Tutorial  

3. キャラクター素材  
   https://brullov.itch.io/generic-char-asset  

---

## 免責事項

1. 本プロジェクトは学習・卒業設計および非商用目的に限定。  
2. 外部素材は各ライセンスに従うこと。  
3. Unity 2021.3.32f1c1 にて動作確認済み。  
4. 利用に伴う損害について作者は責任を負わない。  
5. 再配布時は謝辞を保持すること。  

---