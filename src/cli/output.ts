import chalk from "chalk";

let verboseMode = false;

export function setVerbose(v: boolean): void {
  verboseMode = v;
}

export function renderReasoning(text: string): void {
  if (verboseMode) {
    process.stdout.write(chalk.gray(text));
  }
}

export function renderContent(text: string): void {
  process.stdout.write(text);
}

export function renderToolCall(name: string, args: string): void {
  const shortArgs = args.length > 80 ? args.substring(0, 80) + "..." : args;
  process.stdout.write(chalk.blue(`\n  → ${name}(${shortArgs})`));
}

export function renderToolResult(success: boolean, summary: string): void {
  const marker = success ? chalk.green("[✓]") : chalk.red("[✗]");
  process.stdout.write(` ${marker}\n`);
  if (summary) {
    process.stdout.write(chalk.gray(`    ${summary.substring(0, 200)}\n`));
  }
}

export function renderThinkingStart(): void {
  if (verboseMode) {
    process.stdout.write(chalk.gray("── 思考中... ──\n"));
  }
}

export function renderThinkingEnd(): void {
  if (verboseMode) {
    process.stdout.write("\n");
  }
}

export function renderSeparator(): void {
  process.stdout.write("\n" + chalk.gray("─".repeat(60)) + "\n");
}

export function renderWelcome(model: string): void {
  console.log(chalk.bold(`dcode v0.1.0 — DeepSeek V4 Pro`));
  console.log(chalk.gray(`模型: ${model}`));
  console.log(chalk.gray(`输入 /help 查看帮助，/quit 退出\n`));
}

export function renderHelp(): void {
  console.log(`
${chalk.bold("命令:")}
  /help      显示此帮助
  /quit      退出程序
  /clear     清除对话历史
  /verbose   切换思维链显示

${chalk.bold("快捷键:")}
  Ctrl+C     中断当前操作
  Ctrl+D     退出程序
`);
}
