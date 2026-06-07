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
  startTurn,
  appendReasoning,
  appendContent,
  appendToolCall,
  flushTurn,
  renderReasoningChunk,
  renderContentChunk,
  renderToolCallInline,
  renderWelcome,
  renderHelp,
} from "./output.js";

// ---- 多行粘贴模式 ----
let pasteLines: string[] = [];
let pasteMode = false;

function isPasteDelimiter(line: string): boolean {
  const t = line.trim();
  return t === '"""' || t === "'''" || t === "```";
}

// ---- REPL ----

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
    strict: true,
  });

  let showReasoning = verbose;
  setVerbose(showReasoning);

  setApprovalCallback(async (command: string) => {
    return new Promise((resolve) => {
      console.log(chalk.yellow(`\n⚠ 即将执行: ${command}`));
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
      });
      rl.question(chalk.gray("确认？[y/N] "), (answer) => {
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
    historySize: 200,
  });

  const processInput = async (rawLine: string) => {
    // 多行粘贴模式
    if (pasteMode) {
      if (isPasteDelimiter(rawLine)) {
        pasteMode = false;
        const message = pasteLines.join("\n");
        pasteLines = [];
        rl.setPrompt(chalk.cyan("> "));
        await processMessage(message);
        rl.prompt();
        return;
      }
      pasteLines.push(rawLine);
      return;
    }

    if (isPasteDelimiter(rawLine)) {
      pasteMode = true;
      pasteLines = [];
      rl.setPrompt(chalk.gray("┊ "));
      return;
    }

    const trimmed = rawLine.trim();
    if (!trimmed) return;

    // 内置命令
    if (trimmed.startsWith("/")) {
      switch (trimmed) {
        case "/help":
          renderHelp();
          return;
        case "/quit":
          console.log(chalk.gray("再见 👋"));
          process.exit(0);
        case "/clear":
          contextManager.reset();
          console.log(chalk.gray("对话已清除"));
          return;
        case "/verbose":
          showReasoning = !showReasoning;
          setVerbose(showReasoning);
          console.log(chalk.gray(`思维链: ${showReasoning ? "显示" : "隐藏"}`));
          return;
        default:
          console.log(chalk.gray(`未知: ${trimmed}，/help 查看帮助`));
          return;
      }
    }

    await processMessage(trimmed);
  };

  const processMessage = async (message: string) => {
    startTurn();
    const initialMessages = contextManager.getMessages();

    try {
      const result = await runAgentLoop(
        { userMessage: message },
        {
          client,
          softLimit: config.softLimit,
          hardLimit: config.hardLimit,
          toolRegistry: registry,
          initialMessages,
          onReasoningChunk: (text) => {
            appendReasoning(text);
            renderReasoningChunk(text);
          },
          onContentChunk: (text) => {
            appendContent(text);
            if (showReasoning) renderContentChunk(text);
          },
          onToolCall: (name, args) => {
            appendToolCall(name, args);
            renderToolCallInline(name, args);
          },
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

      flushTurn();

      contextManager.addMessage({ role: "user", content: message });
      contextManager.addMessage({
        role: "assistant",
        content: result.content,
        reasoning_content: result.reasoningContent,
        tool_calls: null,
      });

      if (showReasoning) {
        console.log(
          chalk.gray(
            `[${result.totalRounds}轮 | ${result.usage.promptTokens}↑ ${result.usage.completionTokens}↓]`
          )
        );
      }
      console.log();
    } catch (error) {
      flushTurn();
      console.error(
        chalk.red(
          `错误: ${error instanceof Error ? error.message : String(error)}`
        )
      );
      console.log();
    }
  };

  rl.on("line", async (line) => {
    rl.pause();
    await processInput(line);
    if (!pasteMode) rl.prompt();
    rl.resume();
  });

  rl.on("SIGINT", () => {
    if (pasteMode) {
      pasteMode = false;
      pasteLines = [];
      rl.setPrompt(chalk.cyan("> "));
      console.log(chalk.gray("\n粘贴已取消"));
      rl.prompt();
    } else {
      console.log(chalk.gray("\nCtrl+D 或 /quit 退出"));
      rl.prompt();
    }
  });

  rl.prompt();
}

// ---- 单次执行 ----

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
    strict: true,
  });

  setApprovalCallback(async () => true);
  setVerbose(verbose);

  console.log(chalk.gray(`dcode > ${message}\n`));

  startTurn();

  const result = await runAgentLoop(
    { userMessage: message },
    {
      client,
      softLimit: config.softLimit,
      hardLimit: config.hardLimit,
      toolRegistry: registry,
      onReasoningChunk: (text) => {
        appendReasoning(text);
        renderReasoningChunk(text);
      },
      onContentChunk: (text) => {
        appendContent(text);
        if (verbose) renderContentChunk(text);
      },
      onToolCall: (name, args) => {
        appendToolCall(name, args);
        renderToolCallInline(name, args);
      },
    }
  );

  flushTurn();
  console.log();
}
