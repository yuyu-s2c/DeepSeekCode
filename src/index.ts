#!/usr/bin/env node
import { Command } from "commander";
import chalk from "chalk";
import { loadConfig } from "./config/loader.js";
import { startRepl, runSingleMessage } from "./cli/interface.js";

const program = new Command()
  .name("dcode")
  .description("深度适配 DeepSeek V4 Pro 的编码 CLI 助手")
  .version("0.1.0")
  .argument("[message]", "直接发送消息（省略则进入交互模式）")
  .option("-f, --file <path>", "附加文件到对话上下文")
  .option("-m, --model <name>", "模型名称")
  .option("-r, --max-rounds <n>", "最大对话轮次")
  .option("--resume", "恢复上次会话")
  .option("-v, --verbose", "显示思维链内容")
  .parse();

async function main() {
  const config = loadConfig();
  const options = program.opts();

  if (options.model) config.model = options.model;
  if (options.maxRounds) {
    const val = Number.parseInt(options.maxRounds, 10);
    if (Number.isNaN(val)) {
      throw new Error(`无效的 --max-rounds 值: ${options.maxRounds}`);
    }
    config.maxRounds = val;
  }

  const message = program.args[0];

  if (options.file) {
    console.log(chalk.gray(`(即将附加文件: ${options.file})`));
  }

  const verbose = !!options.verbose;

  if (message) {
    await runSingleMessage(message, verbose);
  } else {
    await startRepl(verbose);
  }
}

main().catch((err) => {
  console.error(chalk.red(`致命错误: ${err.message}`));
  process.exit(1);
});
