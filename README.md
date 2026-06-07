# dcode

深度适配 DeepSeek V4 Pro 的编码 CLI 助手。

## 安装

```bash
git clone <repo-url>
cd dcode-cli
npm install
```

## 使用

```bash
# 设置 API Key
set DEEPSEEK_API_KEY=sk-xxxxx  # Windows
export DEEPSEEK_API_KEY=sk-xxxxx  # Linux/macOS

# 编译
npm run build

# REPL 模式（全屏 TUI）
node dist/index.js

# 单次执行
node dist/index.js "你的问题"

# 显示思维链
node dist/index.js -v "你的问题"
```

## 命令

| 命令 | 说明 |
|------|------|
| `dcode` | 进入 REPL |
| `dcode "消息"` | 单次执行 |
| `dcode -v` | 显示思维链 |
| `dcode -r 100` | 最大 100 轮 |
| `dcode --model deepseek-v4-flash` | 切换模型 |
| `dcode --file path/to/file.ts` | 附加文件 |

### REPL 命令

| 命令 | 说明 |
|------|------|
| `/help` | 帮助 |
| `/quit` | 退出 |
| `/clear` | 清除对话 |
| `/verbose` | 切换思维链 |

## 项目配置

项目根目录可放置 `.dcode.json`：

```json
{
  "model": "deepseek-v4-pro",
  "maxRounds": 50,
  "softLimit": 50,
  "hardLimit": 200,
  "compressThreshold": 0.8,
  "maxTokens": 8192,
  "excludePatterns": ["node_modules", ".git", "dist"]
}
```

## 技术栈

- TypeScript + Node.js 18+
- Ink (React for terminal) + readline
- openai SDK → DeepSeek API
- commander.js

## 项目结构

```
src/
├── index.ts              # CLI 入口
├── config/
│   ├── defaults.ts       # 默认配置
│   └── loader.ts         # 配置加载
├── api/
│   ├── client.ts         # DeepSeek API 客户端
│   ├── message-builder.ts
│   └── retry.ts          # 重试策略
├── agent/
│   ├── loop.ts           # Agent 主循环
│   ├── system-prompt.ts  # 系统提示词
│   └── types.ts
├── tools/
│   ├── registry.ts       # 工具注册中心
│   ├── read_file.ts
│   ├── write_file.ts
│   ├── edit_file.ts
│   ├── run_shell.ts
│   ├── search_content.ts
│   ├── search_files.ts
│   ├── path-utils.ts     # 工作区安全边界
│   └── index.ts
├── context/
│   ├── tokenizer.ts      # Token 估算
│   ├── compressor.ts     # 历史压缩
│   └── manager.ts        # 上下文管理器
└── cli/
    ├── app.tsx           # Ink TUI 组件
    ├── interface.ts      # REPL/单次入口
    └── output.ts         # 流式输出缓冲区
```

## 架构

```
用户输入 → readline → Agent 循环 → DeepSeek API
                         ↕
                    工具注册中心
                  (read/write/edit/shell/search)
                         ↕
                    上下文管理器
                  (KV 缓存 / 压缩 / 持久化)
```

## DeepSeek V4 Pro 适配

- 思考模式 (`thinking: enabled`) + `reasoning_effort` 动态调节
- `reasoning_content` 生命周期管理（有工具回传 / 无工具丢弃）
- KV 硬盘缓存感知的上下文策略（只追加不修改前缀）
- strict 模式 (`/beta` 端点)
- 指数退避重试 (`busy`/`rate_limit`/`server_error`)

## 开发

```bash
npm install
npm run build
npm run dev    # watch 模式
```
