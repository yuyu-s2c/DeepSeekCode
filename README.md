# DeepSeek Code

> Windows 桌面 AI 编程助手，深度适配 DeepSeek V4 Pro

DeepSeek Code 是一个基于 WPF 的本地 AI Agent，专为 DeepSeek API 设计。支持流式对话、工具调用、Thinking 模式、上下文缓存优化等完整功能。

## 特性

- **原生 Windows 桌面应用** — WPF + WebView2，无需浏览器，Windows 10/11 自带运行时
- **DeepSeek V4 深度适配** — 1M 上下文窗口、Thinking 自适应思考、KV Cache 缓存友好策略
- **15 个内置工具** — 文件读写、代码搜索、Shell 执行、Git 操作、网页抓取、子代理分派
- **Plan 模式** — 先规划后执行，5 阶段工作流
- **MCP 协议** — 支持外部工具扩展（JSON-RPC 2.0 over stdio）
- **权限系统** — Allow/Deny/Ask 三级控制，危险命令自动拦截
- **代码渲染** — 流式 Markdown（marked.js）+ 语法高亮（highlight.js）+ 数学公式（KaTeX）

## 快速开始

### 从源码构建

```bash
# 需要有 .NET 10 SDK
git clone https://github.com/yuyu-s2c/DeepSeekCode.git
cd DeepSeekCode
dotnet build

# 打包为单文件 exe（无需 .NET 运行时）
dotnet publish -p:PublishSingleFile=true -c Release -o ./publish
```

### 直接下载

从 Releases 页面下载 `DeepSeekCode.exe`，双击运行。

### 使用

1. 启动后进入设置（齿轮图标），填入 DeepSeek API Key
2. 选择模型：`deepseek-v4-pro`（强力推理）或 `deepseek-v4-flash`（快速响应）
3. 开始对话

## 技术栈

| 层 | 技术 |
|----|------|
| 桌面框架 | WPF (.NET 10) + WebView2 |
| 渲染引擎 | marked.js + highlight.js + KaTeX（离线可用） |
| 架构模式 | EventBus 事件总线 + ServiceLocator DI |
| 安全 | DPAPI 加密存储 + 命令注入防护 |
| 扩展 | MCP 协议 + Slash 命令 + Skills 引擎 |

## 项目结构

```
DeepSeekCode/
├── App.xaml.cs              # 应用入口，服务注册
├── MainWindow.xaml          # 主界面
├── MainWindow.Conversation.cs  # 流式对话核心
├── Models/                  # 数据模型
├── Services/                # 核心服务
│   ├── DeepSeekClient.cs    # API 客户端
│   ├── ConversationManager.cs  # 对话管理
│   ├── ContextStrategy.cs   # 上下文策略（缓存友好）
│   └── EventBus.cs          # 事件总线
├── Tools/                   # 15 个内置工具
├── Commands/                # Slash 命令系统
├── MCP/                     # MCP 协议客户端
├── UI/                      # ChatRenderer、状态栏
└── Resources/js/            # 离线 JS 库
```

## 许可证

MIT
