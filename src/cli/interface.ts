import * as readline from "node:readline";
import chalk from "chalk";
import { loadConfig } from "../config/loader.js";
import { DeepSeekClient } from "../api/client.js";
import { runAgentLoop } from "../agent/loop.js";
import { ContextManager } from "../context/manager.js";
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

export async function startRepl(verbose = false): Promise<void> {
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

  let showReasoning = verbose;
  setVerbose(showReasoning);

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

  const contextManager = new ContextManager();

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
          contextManager.reset();
          console.log(chalk.gray("对话历史已清除"));
          return;
        case "/verbose":
          showReasoning = !showReasoning;
          setVerbose(showReasoning);
          console.log(chalk.gray(`思维链显示: ${showReasoning ? "开启" : "关闭"}`));
          return;
        default:
          console.log(chalk.gray(`未知命令: ${trimmed}，输入 /help 查看帮助`));
          return;
      }
    }

    renderThinkingStart();

    const initialMessages = contextManager.getMessages();

    try {
      const result = await runAgentLoop(
        { userMessage: trimmed },
        {
          client,
          softLimit: config.softLimit,
          hardLimit: config.hardLimit,
          toolRegistry: registry,
          initialMessages,
          onReasoningChunk: (text) => renderReasoning(text),
          onContentChunk: (text) => { if (showReasoning) renderContent(text); },
          onToolCall: (name, args) => renderToolCall(name, args),
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
      if (!showReasoning) {
        console.log(result.content);
      }
      console.log();

      contextManager.addMessage({ role: "user", content: trimmed });
      contextManager.addMessage({
        role: "assistant",
        content: result.content,
        reasoning_content: result.reasoningContent,
        tool_calls: null,
      });

      if (showReasoning) {
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

export async function runSingleMessage(message: string, verbose = false): Promise<void> {
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

  setVerbose(verbose);

  console.log(chalk.gray(`dcode > ${message}\n`));

  const result = await runAgentLoop(
    { userMessage: message },
    {
      client,
      softLimit: config.softLimit,
      hardLimit: config.hardLimit,
      toolRegistry: registry,
      onReasoningChunk: (text) => renderReasoning(text),
      onContentChunk: (text) => { if (verbose) renderContent(text); },
      onToolCall: (name, args) => renderToolCall(name, args),
    }
  );

  if (!verbose) {
    console.log(result.content);
  } else {
    console.log();  // 流式输出后补换行
  }
}
