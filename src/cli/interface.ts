import * as readline from "node:readline";
import chalk from "chalk";
import { loadConfig } from "../config/loader.js";
import { DeepSeekClient } from "../api/client.js";
import { runAgentLoop } from "../agent/loop.js";
import {
  ToolRegistry,
  readFileTool,
  writeFileTool,
  editFileTool,
  runShellTool,
  setApprovalCallback,
  searchContentTool,
  searchFilesTool,
} from "../tools/index.js";
import {
  setVerbose,
  renderReasoning,
  renderContent,
  renderToolCall,
  renderToolResult,
  renderThinkingStart,
  renderThinkingEnd,
  renderSeparator,
  renderWelcome,
  renderHelp,
} from "./output.js";

export async function startRepl(): Promise<void> {
  const config = loadConfig();

  const registry = new ToolRegistry();
  registry.register(readFileTool);
  registry.register(writeFileTool);
  registry.register(editFileTool);
  registry.register(runShellTool);
  registry.register(searchContentTool);
  registry.register(searchFilesTool);

  const client = new DeepSeekClient({
    apiKey: config.apiKey,
    baseUrl: config.baseUrl,
    model: config.model,
    maxTokens: config.maxTokens,
  });

  let verbose = false;
  setVerbose(verbose);

  setApprovalCallback(async (command: string) => {
    return new Promise((resolve) => {
      console.log(chalk.yellow(`\n⚠ 即将执行命令: ${command}`));
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
      });
      rl.question(chalk.gray("确认执行？[y/N] "), (answer) => {
        rl.close();
        resolve(answer.toLowerCase() === "y" || answer.toLowerCase() === "yes");
      });
    });
  });

  renderWelcome(config.model);

  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
    prompt: chalk.cyan("> "),
    terminal: true,
  });

  const processInput = async (input: string) => {
    const trimmed = input.trim();
    if (!trimmed) return;

    if (trimmed.startsWith("/")) {
      switch (trimmed) {
        case "/help":
          renderHelp();
          return;
        case "/quit":
          console.log(chalk.gray("再见！"));
          process.exit(0);
        case "/clear":
          console.log(chalk.gray("对话历史已清除"));
          return;
        case "/verbose":
          verbose = !verbose;
          setVerbose(verbose);
          console.log(chalk.gray(`思维链显示: ${verbose ? "开启" : "关闭"}`));
          return;
        default:
          console.log(chalk.gray(`未知命令: ${trimmed}，输入 /help 查看帮助`));
          return;
      }
    }

    renderThinkingStart();

    try {
      const result = await runAgentLoop(
        { userMessage: trimmed },
        {
          client,
          softLimit: config.softLimit,
          hardLimit: config.hardLimit,
          toolRegistry: registry,
          onRoundExceeded: async (round) => {
            return new Promise((resolve) => {
              const askRl = readline.createInterface({
                input: process.stdin,
                output: process.stdout,
              });
              askRl.question(
                chalk.yellow(`\n已执行 ${round} 轮，继续？[Y/n] `),
                (answer) => {
                  askRl.close();
                  resolve(answer.toLowerCase() !== "n");
                }
              );
            });
          },
        }
      );

      renderThinkingEnd();
      renderSeparator();
      console.log(result.content);
      console.log();

      if (verbose) {
        console.log(
          chalk.gray(
            `[${result.totalRounds} 轮 | 输入 ${result.usage.promptTokens} tokens | 输出 ${result.usage.completionTokens} tokens]`
          )
        );
      }
    } catch (error) {
      renderThinkingEnd();
      console.error(
        chalk.red(
          `错误: ${error instanceof Error ? error.message : String(error)}`
        )
      );
    }
  };

  rl.on("line", async (line) => {
    rl.pause();
    await processInput(line);
    rl.prompt();
    rl.resume();
  });

  rl.on("SIGINT", () => {
    console.log(chalk.gray("\n使用 Ctrl+D 或 /quit 退出"));
    rl.prompt();
  });

  rl.prompt();
}

export async function runSingleMessage(message: string): Promise<void> {
  const config = loadConfig();

  const registry = new ToolRegistry();
  registry.register(readFileTool);
  registry.register(writeFileTool);
  registry.register(editFileTool);
  registry.register(runShellTool);
  registry.register(searchContentTool);
  registry.register(searchFilesTool);

  const client = new DeepSeekClient({
    apiKey: config.apiKey,
    baseUrl: config.baseUrl,
    model: config.model,
    maxTokens: config.maxTokens,
  });

  setApprovalCallback(async () => true);

  console.log(chalk.gray(`dcode > ${message}\n`));

  const result = await runAgentLoop(
    { userMessage: message },
    {
      client,
      softLimit: config.softLimit,
      hardLimit: config.hardLimit,
      toolRegistry: registry,
    }
  );

  console.log(result.content);
}
