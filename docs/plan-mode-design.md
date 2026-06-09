# Claude Code Plan 模式设计分析

> 来源：Fiddler 抓包 `POST https://api.deepseek.com/anthropic/v1/messages`
> 日期：2026-06-08

---

## 1. 触发机制

Plan 模式不是默认开启的。用户输入 `/plan` 或 Claude 自行判断需要规划时，调用 `EnterPlanMode` 工具，系统在**当前对话轮次**动态注入 Plan mode 系统消息。

关键：Plan mode 提示词是作为 `system` 消息**中途追加**到对话中的，不是启动时就有的。

---

## 2. Plan Mode 系统消息结构

```
system[2]: Plan mode is active...

## Plan File Info
- 指定计划文件路径（如 C:\Users\...\.claude\plans\<slug>.md）
- 使用 Write 工具创建/编辑计划文件
- NOTE: 这是唯一允许编辑的文件

## Plan Workflow（5 阶段）
Phase 1: Initial Understanding  — 理解需求 + 探索代码（只读）
Phase 2: Design                — 启动 Plan 子代理设计实现方案
Phase 3: Review                — 审查方案，向用户提问澄清
Phase 4: Final Plan            — 将最终方案写入计划文件
Phase 5: ExitPlanMode          — 调用 ExitPlanMode 请求用户审批
```

---

## 3. Plan Workflow 各阶段详解

### Phase 1: Initial Understanding（理解阶段）

**目标**: 全面理解用户需求，阅读代码

**规则**:
- 只能使用 Explore 子代理（只读）
- 最多并行启动 3 个 Explore agent
- 1 个 agent: 任务范围明确、用户指定了文件路径
- 多个 agent: 范围不确定、涉及多个代码区域
- 搜索现有函数/工具/模式，避免重复造轮子

### Phase 2: Design（设计阶段）

**目标**: 设计实现方案

**规则**:
- 启动 1 个 Plan agent 设计方案
- 向 agent 提供 Phase 1 的全部探索结果
- 描述需求和约束
- 默认启动至少 1 个 Plan agent（除非是 typo fix 等极简单任务）

### Phase 3: Review（审查阶段）

**目标**: 审查方案并确保符合用户意图

**规则**:
- 阅读 agent 识别的关键文件加深理解
- 确保方案与用户原始需求对齐
- 使用 AskUserQuestion 澄清剩余疑问

### Phase 4: Final Plan（最终方案）

**目标**: 将方案写入计划文件

**规则**:
- 以 **Context** 段落开头：为什么做这个改动、要解决的问题
- 只包含推荐方案，不列所有备选
- 简洁但足够详细以供执行
- 列出要修改的关键文件；重复模式描述一次即可
- 引用要复用的现有函数和工具
- 包含验证步骤（如何端到端测试）

### Phase 5: ExitPlanMode（退出计划模式）

**目标**: 请求用户审批

**规则**:
- Turn 结束时只能做两件事之一：AskUserQuestion 或 ExitPlanMode
- **严禁**用文字问"这个计划可以吗？"——只能用 ExitPlanMode
- ExitPlanMode 读取之前写入的计划文件内容展示给用户

---

## 4. EnterPlanMode 工具定义

### 何时必须用（7 种场景）

| 场景 | 示例 |
|------|------|
| 新功能 | "Add a logout button" |
| 多种方案可选 | "Add caching to the API" |
| 修改现有行为 | "Update the login flow" |
| 架构决策 | "Add real-time updates" |
| 多文件改动（>3 文件） | "Refactor the authentication system" |
| 需求不明确 | "Make the app faster" |
| 用户偏好影响方案 | 多种合理实现路径 |

### 何时不用（4 种反例）

| 场景 | 原因 |
|------|------|
| 单行 bug fix / typo | 不需要规划 |
| 单一函数 + 需求明确 | 直接执行 |
| 用户给了极详细的指令 | 无需再设计 |
| 纯调研/探索任务 | 用 Explore agent 代替 |

---

## 5. ExitPlanMode 工具定义

**作用**: 信号工具，告诉用户"计划已完成，请审批"

**规则**:
- 计划内容已写入计划文件，此工具只是读文件并展示
- 不要用 AskUserQuestion 问"这个计划可以吗？"
- 仅用于需要写代码的任务；纯调研任务不用
- `allowedPrompts` 参数可预设实现阶段需要的权限

---

## 6. 设计要点总结

### 6.1 提示词注入时机
不是启动时加载，而是 **EnterPlanMode 被调用后**，系统在下一轮对话中追加一条 system 消息。我们的实现也可以这样做——在 `HandleInputAsync` 中检测 Plan 模式状态，如果激活则追加 Plan mode prompt。

### 6.2 工具约束
Plan 模式下 AI 被**强制禁止**修改性操作：
```
you MUST NOT make any edits... run any non-readonly tools
(including changing configs or making commits)
or otherwise make any changes to the system
```

唯一例外：**计划文件本身**可以用 Write/Edit 修改。

### 6.3 子代理驱动规划
不是 AI 自己规划，而是**启动 Plan 子代理**来设计方案。这种"元认知"分离确保方案更客观。

### 6.4 Phase 与 Turn 的关系
整个 5 阶段在**一个 turn** 内完成。AI 在同一个 turn 里执行 Phase 1-4，Phase 5 (ExitPlanMode) 结束 turn。用户审批后进入实现阶段。

### 6.5 计划文件
计划存储在独立文件中（`~\.claude\plans\<slug>.md`），既是工作产物，也是审批依据。

---

## 7. 对我们的实现建议

| 组件 | 实现方式 |
|------|----------|
| 模式切换 | `/plan` 命令或 AI 自行调用 enter_plan_mode |
| Plan prompt 注入 | 在 `HandleInputAsync` 中检测 `_planModeActive`，追加到 system 消息 |
| 工具约束 | Plan 模式下，权限系统自动 Deny 所有写操作（写入计划文件除外） |
| 计划文件 | 存到 `{workspace}\.deepseek-code\plans\` 目录 |
| 子代理 | 复用现有 SubagentRunner（explore + plan 模式） |
| ExitPlanMode | 新增工具或 Slash 命令，展示计划文件内容 |
