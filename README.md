# Just Neko

Windows 上的 AI 桌宠。启动后角色出现在桌面右下角，任务栏上没有它的按钮，只在托盘里有一个猫头图标。

**角色只管互动。** 单击角色摸摸头（她会开心一下），双击跟她聊天，拖动可以搬到别处；右键菜单里有聊天、摸摸头、清空对话和「藏到托盘」。聊天时角色旁边只弹出一个小输入框，回复在头顶的气泡里逐段说出来，说完停留 5 秒再渐隐。想翻之前的对话，点输入框左边的「≡」展开聊天记录（还能再点「展开」看更长的），记录挡住气泡时气泡会挪到角色右侧。聊天记录默认保存在本机，重启后还在。模型连不上时角色头上会出现一个小「!」，点它直接打开对应的设置。

**托盘是后台。** 左键显示或藏起角色；右键菜单里有勿扰模式、设置中心、本地模型状态和退出。设置中心分六页：对话（模型服务、测试连接、角色提示词）、本地模型（下载和状态）、角色（人物大小、高级的动作绑定）、行为（全屏时自动隐藏、勿扰）、隐私（聊天记录、交互留痕）、关于。前台有全屏程序时角色会自动藏起来；勿扰时角色不会自己走动或主动搭话。

第一次启动有引导：本机已经在跑 Ollama 就直接用它；没有的话可以下载内置本地模型、填云端服务，或者先用离线演示。

## 本机启动

项目位于 `C:\Dev\just-neko`，使用 Godot 4.7.2 .NET。当前电脑可以在项目目录运行：

```powershell
dotnet build
& 'C:\Dev\Tools\Godot\4.7.2-dotnet\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe' --path $PWD.Path
```

也可以用 Godot .NET 编辑器打开 `project.godot`，按 F5 运行主场景。

## 接入自己的模型

从托盘菜单打开「设置中心 → 对话」，选择服务、点「测试连接」确认能用，再点「应用」。每种服务各记一份地址、模型和 Key，切换过去再切回来不用重填。设置保存在用户目录的 `settings.json`；Key 在 Windows 上用当前用户的 DPAPI 加密后单独保存，不会以明文落盘。

- **内置本地模型（默认）**。桌宠自带 llama.cpp 的 `llama-server`（Vulkan 版，AMD / NVIDIA / Intel 显卡都能加速，没有显卡时用 CPU）和 Qwen3 1.7B（Q4_K_M，约 1.1 GB）。第一次使用时下载到 `%LOCALAPPDATA%\JustNeko\local-model`，可以取消，下次接着下，下完按 SHA256 校验；HuggingFace 连不上时自动换国内镜像。聊天时桌宠自己启动 `llama-server`（只监听 127.0.0.1，关掉了思考模式），退出时一起关掉，桌宠崩溃时系统也会把它收掉。版本固定在 `core/LocalModel/LocalModelManifest.cs`。
- **本地 Ollama**。启动 Ollama 就能用。模型留空时自动用本机已安装的第一个模型，也可以点「检测」从已安装的模型里选。默认地址 `http://localhost:11434/v1`，Key 可留空。
- **OpenAI 兼容服务**。填服务提供的 API 基础地址、模型名和 Key，需要支持流式 `chat/completions`，例如 OpenAI 的基础地址为 `https://api.openai.com/v1`。
- **离线演示**。不联网，用来看聊天、表情和停止按钮怎么动。
- 可编辑角色提示词。应用把近期对话、当前消息和角色可用的表情名称一起发给模型。模型用 `[emotion:exp_06]` 或配置好的 `[emotion:shy]` 调用角色已有的表情，应用把标签解析为表情，正文显示在气泡里。

## 代码结构

代码分两部分。`core/` 是独立的纯 C# 类库（`JustNeko.Core.csproj`），不引用 Godot，放 AI、交互编排、设置和平台接口；`src/` 是 Godot 这一侧，放角色表现和界面。以后换平台、写单元测试、接 Agent，都只依赖 `core/`。

| 目录 | 内容 |
|---|---|
| `core/Ai` | 模型接口统一为 `Microsoft.Extensions.AI` 的 `IChatClient`，OpenAI 兼容服务和 Ollama 走官方 OpenAI SDK；`ModelRouter` 按任务从设置里选服务和模型 |
| `core/Interaction` | `InteractionOrchestrator` 管一次交互的全过程：交互 ID、优先级、取消、丢弃迟到事件；`DirectiveParser` 从流里拆出 `[名称:值]` 指令；`InteractionLog` 把每次交互写进 `logs/interactions-日期.jsonl`（只记耗时和结果，不记内容）；`ChatHistoryStore` 保存聊天记录 |
| `core/LocalModel` | 内置本地模型：`LocalModelManifest` 固定下载哪些文件，`LocalModelInstaller` 负责续传、校验、换镜像和磁盘空间检查，`LocalModelManager` 管下载状态和 `llama-server` 进程 |
| `core/Settings` | `settings.json` 的读写，Key 交给平台的凭据存储 |
| `core/Platform` | `IPlatformServices`：凭据存储、隐藏任务栏按钮、隐藏窗口、全屏检测、子进程跟随退出。Windows 已实现；其他平台先用通用实现（Key 只留在内存、窗口挪到屏幕外），以后补 macOS 钥匙串、Linux libsecret |
| `src/Pet` | `PetAvatar` 渲染角色；`BehaviorCatalog` 登记行为槽；`PetMotionDirector` 按优先级决定播哪个动作（拖动 > 一次性反应 > 交互阶段 > 自主活动 > 待机） |
| `src/Pet/Activities` | 自主活动框架：`IPetActivity` + `ActivityDirector` |
| `src/Pet` 角色后端 | `PetAvatar` 是角色后端的基类（表情、动作、命中判断、通用层姿态），`Live2DAvatar` 和 `Vrm/VrmAvatar` 各实现一份；`Characters` 列出可选角色并按设置创建，`DesktopPet.SwitchCharacter` 原地替换角色节点实现热切换。VRM 端：`VrmExpressions` 按权重叠加 VRM 表情，`VrmPoser` 每帧改骨骼姿态（垂手、呼吸、看向、倾斜） |
| `src/Pet/Rig` | 通用层，和角色格式无关：`LookAtCursor` 看鼠标、`DragSway` 拖动晃动（弹簧阻尼）、`TextMouth` 按文字节奏对口型（以后换成按语音音量），合成 `AvatarPose`（看向、倾斜、张嘴）。`Live2DRig` 把它叠加到 Live2D 参数上：默认用标准参数，嘴型读 model3.json 的 `LipSync` 分组，不符合标准的角色在模型目录放 `rig.json` 覆盖映射 |
| `src/Shell` | 主场景 `DesktopPet`（组装各模块、鼠标操作、窗口跟随、显示和隐藏）、`TrayIcon` 托盘、`OnboardingWindow` 首次引导、聊天窗口、气泡和头顶小标记 |
| `src/Shell/Settings` | 设置中心 `SettingsCenter` 和六个分页；加一页就是写一个 `SettingsPage` 子类，在 `SettingsCenter` 里登记 |
| `assets/icon.svg` | 程序和托盘图标 |

**加一个自主活动**（比如沿任务栏走）：实现 `IPetActivity`，在 `DesktopPet._Ready` 创建设置中心之前用 `activities.Register(new 你的活动(), behaviors)` 登记。活动在 `Slots` 里声明自己要的行为槽（比如「走路」），玩家就能在「设置中心 → 角色 → 动作绑定」里给它选动作；`Start` 里用 `context.HoldMotion` 播动作，`Update` 里用 `context.Body.MoveTo` 移动角色，`Body.ScreenRect` 和 `Body.WorkArea` 之差就是任务栏所在的边。角色空闲 20–60 秒后按权重抽一个活动来做；用户一拖动、开聊天或开始 AI 交互，活动会被停掉；勿扰时不会开始。AI 指令或 Agent 工具可以用 `ActivityDirector.TryStart(id)` 直接触发。

**加一种 AI 指令**：往 `InteractionOrchestrator.Directives` 里登记名称（比如 `action`），然后在 `DesktopPet.OnInteraction` 里处理对应的 `DirectiveReceived`。

**加一种交互来源**（习惯、看屏幕、Agent）：构造 `InteractionRequest` 交给 `InteractionOrchestrator.Submit`，用 `Priority` 决定能不能打断正在进行的交互（用户输入是 100）。气泡、表情、动作、聊天记录和留痕都会自动跟上。

## AI 流程

已经跑通「发送消息 → 组装提示词和近期对话 → 按设置选择服务 → 流式文字和表情 → 完成或停止」，这条流程由 `core/` 里的交互编排器负责，聊天窗口、气泡和角色都只订阅它的事件。

后续以一次交互为单位组织流程。用户发消息、主动让桌宠看屏幕、习惯到期都可发起交互。点头、跟视、悬停等能在本地立即完成的反应直接由 Godot 执行。需要生成回应的事件才进入异步流程。

1. **准备输入。** 收集当前事件和短期上下文，按需要查 RAG 或选择视觉模型。提示词按应用任务模板、可编辑角色设定、用户长期指令组装。检索到的记忆和屏幕内容作为参考数据传入。
2. **按任务路由。** 文本、视觉、Embedding 和 TTS 各自绑定到服务和模型，几项任务可以共用同一连接。优先让聊天回复附带表情，记忆提取可先复用对话模型。每个服务适配器声明流式、图像或音频能力。
3. **边收边展示。** 把文本增量立即交给气泡。解析允许的表情和动作后交给角色。稳定句段进入 TTS 队列，按顺序播放，口型跟实际播放时间。每次交互有独立 ID 和取消信号。停止时取消网络请求和语音队列，并丢弃该轮迟到的事件。
4. **按结果写回。** 完整回复进入近期对话。明确的「请记住」直接保存，普通聊天只在后台提取可能有长期价值的条目，记录来源和确认状态。用户纠正和删除后更新检索索引。屏幕观察默认只在当前交互使用。

下一步先验证一条真实模型的「发消息、边出字边说话、按停止立即停声」。再接单帧视觉和一条可编辑、可跨重启检索的记忆。最后把习惯调度接到同一个入口，统一处理静默时段、冷却、用户输入优先级和表情动作。

### Agent 和玩家插件

加入第一项工具调用时，将模型接口统一到 `Microsoft.Extensions.AI.IChatClient`，继续保留离线演示和 OpenAI 兼容入口。应用管理交互优先级、取消和权限。生成回复时，可按当前交互给模型提供已启用的工具。需要多步计划、等待确认或流程恢复时，再在这一层接入 Microsoft Agent Framework。 [IChatClient](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient) · [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/overview)

- **角色包**使用带版本的 JSON，包含角色设定、表情映射和用户可启用的习惯。比如玩家可以分享一个「专注陪伴」角色，附带定时提醒和自己的语气。应用解析成内置事件和动作。
- **工具插件**通过 MCP 接入。用官方 C# SDK 连接玩家配置的本地进程或远端 HTTP 服务，读取工具列表和输入结构。插件设置显示工具用途、启用状态和所需权限。应用按会话提供工具，并在调用前核对权限。外部工具进程单独运行，连接所需凭据由用户配置。 [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) · [本地和远端传输方式](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)

建议用「把番茄钟剩余时间告诉我」验收首个插件。模型选择一个只读查询，应用检查后执行，将结果交给模型继续流式回答。鼠标交互和定时习惯继续由本地规则触发，能依据结果做表情或播放动作。

## 角色：Live2D 和 VRM

在「设置中心 → 角色」里选角色，选好立即切换，不用重启。两种格式共用同一套互动、表情、看鼠标、拖动晃动和对口型。

- **Live2D**：模型放在 `Models/`（见下一节），表情和动作来自模型文件。
- **VRM（3D）**：把 `.vrm` 放进 `Models/vrm/`，用 Godot 编辑器打开一次项目就会自动导入（插件 [godot-vrm](https://github.com/V-Sekai/godot-vrm)，固定在 `e15199f9`，MIT）。表情用 VRM 标准表情（开心、生气、难过、放松、惊讶，Live2D 常用的 curious / shy / sleepy 会自动映射过去），口型用 `aa`；VRM 没有动作文件，待机时手臂自然垂下、呼吸、眨眼都由程序生成。头发衣服靠模型自带的弹簧骨骼摆动。试过的示例角色：pixiv 的 `VRM1_Constraint_Twist_Sample.vrm`（允许商用、再分发和修改）和 VRM 联盟的 `Seed-san.vrm`（VRM Public License 1.0），和 Live2D 模型一样不进仓库。
- godot-vrm 对「节点约束」（袖子、关节扭转）的导入不可靠，会把袖子算回水平，所以加载时移除约束节点，由程序带着和上臂朝向一致的骨骼（袖子、扭转骨骼）跟手臂走；对普通 VRoid 模型没有影响。
- 运行时直接加载 `.vrm`（不经编辑器导入）在这个版本的插件上不可用：插件把骨骼改名成 Godot 人形骨骼名后，蒙皮和表情绑定还指向旧名字，编辑器导入时由 Godot 的导入流程补上，运行时没有这一步。用户自己放入的角色要等这个问题解决后再支持。

## Live2D（可选）

本机已准备 [gd_cubism v0.9.1](https://github.com/MizunagiKB/gd_cubism/releases/tag/v0.9.1) 源码与 SCons。项目的 Windows 扩展使用 [Live2D 官方 Native SDK 5 R5](https://www.live2d.com/en/sdk/download/native/)，安装脚本会应用 R5 兼容补丁，并编译供编辑器和原型使用的扩展。导出发行构建时加 `-BuildRelease`。

```powershell
& .\tools\Install-Cubism.ps1 -SdkArchive 'D:\Edge下载\CubismSdkForNative-5-r.5.zip'
```

把有权使用的模型运行时文件放到 `Models` 下，将入口命名为 `neko.model3.json`，保留 JSON 中引用的纹理、物理和表情的相对路径。应用会读取模型本身的表情 ID。同目录可添加 `expressions.json`，为有效的 ID 配置语义别名，例如 `{"curious":"f01","happy":"f02"}`。Live2D 用 2 倍分辨率渲染后缩回原窗口尺寸。扩展或模型不可用时显示内置预览猫。

R5 补丁沿用 gd_cubism 对普通、加法和乘法混合的渲染支持。Cubism 5.3 添加的混合模式和离屏渲染需要单独适配。Native SDK、编译的扩展、本机模型、API Key 和 Godot 缓存均不进入 Git。
