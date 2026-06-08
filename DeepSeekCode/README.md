# DeepSeek Code

100% 适配 DeepSeek V4 Pro 的本地桌面 AI Agent。C# + WPF 构建，Windows 原生体验。

## 技术栈

| 层 | 选型 |
|----|------|
| 语言 | C# 13 |
| 框架 | .NET 10 + WPF |
| Markdown | Markdig → FlowDocument |
| HTTP | System.Net.Http（SSE 流式） |
| 语法高亮 | ColorCode.Core + HtmlAgilityPack |
| Web 抓取 | HtmlAgilityPack（正文提取） |
| 配置 | JSON（`%USERPROFILE%\.deepseek-code\`） |
| 打包 | `dotnet publish --self-contained` → 单 exe |

## 快速开始

```bash
# 编译
dotnet build

# 运行
dotnet run --project DeepSeekCode

# 打包（单文件 exe）
dotnet publish -c Release --self-contained -p:PublishSingleFile=true
```

首次启动会弹出 API Key 配置窗口。Key 保存在 `%USERPROFILE%\.deepseek-code\config.json`。

## 功能

- DeepSeek V4 API 完整适配（Thinking 模式、reasoning_content、JSON Output、FIM、Cache 读取）
- 流式 SSE 对话 + Thinking 面板（右侧可折叠，实时展示推理过程）
- Function Calling 工具调用（自动多轮调用 → 结果回传）
- Markdown 实时渲染 + 代码语法高亮（30+ 语言，ColorCode 引擎）
- **13 个内置工具**：`read_file` `edit_file` `write_file` `glob` `grep` `shell` `webfetch` `read_skill` `git_diff` `git_log` `git_commit` `todo_write` `task`
- 子代理系统：线程池并行分派，V4 Flash 模型，explore/general 双模式
- Todo 追踪：AI 自动创建/更新任务列表，进度面板可视化
- Diff 预览：文件编辑前后对比，绿色(+)/红色(-) 行级渲染
- 三级权限系统（Allow / Deny / Ask）
- Slash 命令系统（`/help` `/clear` `/model` `/save` `/load` `/settings` `/workspace` `/skills`）
- 项目指令自动加载（`DEEPSEEK.md`）
- Skills 技能系统（Markdown + Frontmatter，34 个技能已迁移，按需加载）
- 会话持久化 + 输入历史（↑↓ 翻历史）+ 状态栏（Token/缓存命中率）
- 快捷键（Ctrl+Enter 发送 / Ctrl+L 清屏）+ 消息右键菜单（复制/重新发送）
- 进度指示器：状态栏 spinner 动画（AI 思考中 / 工具执行中 / 完成）
- 暗色主题（VS Code 风格）

## 命令

| 命令 | 说明 |
|------|------|
| `/help` | 显示帮助 |
| `/clear` | 清空当前对话 |
| `/model <name>` | 切换模型（deepseek-v4-pro / deepseek-v4-flash） |
| `/save [name]` | 保存会话 |
| `/load <id>` | 加载历史会话 |
| `/config [key] [value]` | 查看/修改配置 |
| `/compact` | 压缩上下文窗口 |
| `/skills [name]` | 列出或查看已加载技能 |
| `/settings` | 打开设置窗口 |

## 配置

### 用户级（`~\.deepseek-code\config.json`）

```json
{
  "apiKey": "sk-xxx",
  "apiBaseUrl": "https://api.deepseek.com",
  "model": "deepseek-v4-pro",
  "maxTokens": 8192,
  "temperature": 0.7
}
```

### 项目级（项目根目录 `.deepseek-code\project.json`）

```json
{
  "model": "deepseek-v4-flash",
  "systemPrompt": "自定义系统提示词...",
  "maxContextTokens": 64000,
  "includeFiles": ["*.cs"],
  "excludeFiles": ["obj/**", "bin/**"]
}
```

项目级配置优先级高于用户级。

## 项目结构

```
DeepSeekCode/
├── Services/       # 核心服务（API、对话、事件、配置、上下文、子代理）
├── Tools/          # 工具系统（接口、注册、管线、13 个实现）
├── Permission/     # 权限系统（三级权限 + 默认规则）
├── Commands/       # Slash 命令（接口 + 内置命令）
├── Session/        # 会话持久化
├── Skills/         # 技能引擎
├── Markdown/       # Markdown 渲染器
├── Diff/           # Diff 渲染器（行级对比 + 彩色输出）
├── UI/             # UI 组件（状态栏 VM）
├── Models/         # 数据模型（配置、消息、工具定义等）
└── FEATURES.md     # 功能规划与进度跟踪
```

## 开发

```bash
# 还原依赖
dotnet restore

# 编译（Debug）
dotnet build

# 监视文件变更自动编译
dotnet watch build

# 发布 Release
dotnet publish -c Release
```

详细架构设计见 [ARCHITECTURE.md](ARCHITECTURE.md)。
