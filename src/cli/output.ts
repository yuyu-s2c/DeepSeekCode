import chalk from "chalk";

let verboseMode = false;
let bufferedReasoning = "";
let bufferedContent = "";
let currentToolCalls: Array<{ name: string; args: string }> = [];

export function setVerbose(v: boolean): void {
  verboseMode = v;
}

export function startTurn(): void {
  bufferedReasoning = "";
  bufferedContent = "";
  currentToolCalls = [];
}

export function appendReasoning(text: string): void {
  bufferedReasoning += text;
}

export function appendContent(text: string): void {
  bufferedContent += text;
}

export function appendToolCall(name: string, args: string): void {
  currentToolCalls.push({ name, args });
}

// ---- 输出 ----

export function flushTurn(): void {
  // 思考框
  if (verboseMode && bufferedReasoning) {
    const w = Math.min(process.stdout.columns - 4 || 76, 76);
    console.log();
    console.log(chalk.gray(`┌${"─".repeat(w - 2)}┐`));
    const lines = bufferedReasoning
      .replace(/\n+$/, "")
      .split("\n")
      .filter((l) => l.trim())
      .slice(0, 20);
    for (const line of lines) {
      for (let i = 0; i < line.length; i += w - 4) {
        console.log(chalk.gray(`│ ${line.substring(i, i + w - 4).padEnd(w - 4)} │`));
      }
    }
    console.log(chalk.gray(`└${"─".repeat(w - 2)}┘`));
  }

  // 工具调用
  for (const tc of currentToolCalls) {
    const shortArgs = tc.args.length > 70 ? tc.args.substring(0, 67) + "..." : tc.args;
    console.log(chalk.blue(`  ⚙ ${tc.name}(${shortArgs})`));
  }

  // 回复
  if (bufferedContent) {
    if (verboseMode || currentToolCalls.length > 0) console.log();
    console.log(bufferedContent.trimEnd());
  }
}

// ---- 实时流式 ----

export function renderReasoningChunk(text: string): void {
  if (verboseMode) process.stdout.write(chalk.gray(text));
}

export function renderContentChunk(_text: string): void {
  // 统一在 flushTurn 输出
}

export function renderToolCallInline(_name: string, _args: string): void {
  // 统一在 flushTurn 输出，避免显示两次
}

// ---- 静态 ----

export function renderWelcome(model: string): void {
  console.log(chalk.bold.cyan(`  dcode`) + chalk.gray(` — DeepSeek V4 Pro`));
  console.log(chalk.gray(`  模型: ${model}  |  /help 帮助  |  /quit 退出`));
  console.log(chalk.gray(`  ↑↓ 历史  |  Ctrl+C 中断  |  """ 多行粘贴`));
  console.log();
}

export function renderHelp(): void {
  console.log(`
${chalk.bold("命令")}
  /help        帮助
  /quit        退出
  /clear       清除对话
  /verbose     切换思维链

${chalk.bold("输入")}
  ↑↓           浏览历史
  """ ... """  粘贴多行
  Ctrl+C       中断

${chalk.bold("启动")}
  dcode              REPL
  dcode "消息"       单次
  dcode -v           思维链
`);
}
