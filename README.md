# Just Neko

Windows 桌宠的第一版 AI 交互原型。启动后，角色出现在桌面右下角。左键点角色会在左下侧打开聊天，发送和收到的消息保留在聊天记录中。头顶的独立气泡逐段显示当前回复，回复结束后停留 5 秒再渐隐。拖动角色时，聊天和气泡都会跟着移动。右键菜单可以打开设置、清空对话或退出。点 × 或按 Esc 可以收起聊天，生成中的回复会继续显示在头顶；要中断生成，点「停止」。

双击角色，在右侧打开或收起「角色属性」。人物大小可在 60%–180% 间实时调整，大小和动作配置自动保存到本机。待机、打开聊天、收起聊天、拖动角色、等待回复、回复中和回复完成各有一项动画绑定。下拉列表来自当前 Live2D 模型 `FileReferences.Motions` 中已注册的动作文件。Mao 的默认绑定在 `config/pet-motion-defaults.cfg`，玩家保存的选择优先。待机、拖动、等待及回复中会在对应阶段循环播放，其余动作播放一次。

## 本机启动

项目位于 `C:\Dev\just-neko`，使用 Godot 4.7.2 .NET。当前电脑可以在项目目录运行：

```powershell
dotnet build
& 'C:\Dev\Tools\Godot\4.7.2-dotnet\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe' --path $PWD.Path
```

也可以用 Godot .NET 编辑器打开 `project.godot`，按 F5 运行主场景。

## 接入自己的模型

- 从角色右键菜单打开「设置…」，选择服务并点「应用」。默认的「离线演示」可以直接发送消息，验证聊天、表情和停止按钮。
- **本地 Ollama**。启动 Ollama，选择已安装的聊天模型，填写模型名。默认地址 `http://localhost:11434/v1`，Key 可留空。
- **OpenAI 兼容服务**。填服务提供的 API 基础地址、模型名和 Key。需要支持流式 `chat/completions`。例如 OpenAI 的基础地址为 `https://api.openai.com/v1`。
- 可编辑角色提示词。应用把近期对话、当前消息和角色可用的表情名称一起发给选择的模型。本地和云端模型都可用 `[emotion:exp_06]` 或配置好的 `[emotion:shy]` 调用角色已有的表情。应用把标签解析为表情，正文显示在气泡里。设置和 Key 只保留在本次运行。

## AI 流程

初版已经跑通「发送消息 → 组装提示词和近期对话 → 选择离线演示或兼容接口 → 流式文字和表情 → 完成或停止」。现有 HTTP 客户端独立于 Godot，聊天窗口负责把结果交给界面和角色预览。

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

## Live2D（可选）

本机已准备 [gd_cubism v0.9.1](https://github.com/MizunagiKB/gd_cubism/releases/tag/v0.9.1) 源码与 SCons。项目的 Windows 扩展使用 [Live2D 官方 Native SDK 5 R5](https://www.live2d.com/en/sdk/download/native/)，安装脚本会应用 R5 兼容补丁，并编译供编辑器和原型使用的扩展。导出发行构建时加 `-BuildRelease`。

```powershell
& .\tools\Install-Cubism.ps1 -SdkArchive 'D:\Edge下载\CubismSdkForNative-5-r.5.zip'
```

把有权使用的模型运行时文件放到 `Models` 下，将入口命名为 `neko.model3.json`，保留 JSON 中引用的纹理、物理和表情的相对路径。应用会读取模型本身的表情 ID。同目录可添加 `expressions.json`，为有效的 ID 配置语义别名，例如 `{"curious":"f01","happy":"f02"}`。Live2D 用 2 倍分辨率渲染后缩回原窗口尺寸。扩展或模型不可用时显示内置预览猫。

R5 补丁沿用 gd_cubism 对普通、加法和乘法混合的渲染支持。Cubism 5.3 添加的混合模式和离屏渲染需要单独适配。Native SDK、编译的扩展、本机模型、API Key 和 Godot 缓存均不进入 Git。
