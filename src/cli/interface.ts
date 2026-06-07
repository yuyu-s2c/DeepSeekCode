import { render } from "ink";
import React from "react";
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
import App from "./app.js";
import {
  startTurn,
  appendReasoning,
  appendContent,
  appendToolCall,
  flushTurn,
  renderReasoningChunk,
  renderToolCallInline,
} from "./output.js";

// REPL 模式：Ink 全屏 TUI
export async function startRepl(verbose = false): Promise<void> {
  const { waitUntilExit } = render(React.createElement(App, { verbose }));
  await waitUntilExit();
}

// 单次模式：行式输出（Ink 在管道/stdin 非 TTY 时不可用）
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

  console.log(chalk.gray(`dcode > ${message}\n`));
  startTurn();

  try {
    const result = await runAgentLoop(
      { userMessage: message },
      {
        client,
        softLimit: config.softLimit,
        hardLimit: config.hardLimit,
        toolRegistry: registry,
        onReasoningChunk: (text) => {
          appendReasoning(text);
          if (verbose) renderReasoningChunk(text);
        },
        onContentChunk: (text) => {
          appendContent(text);
        },
        onToolCall: (name, args) => {
          appendToolCall(name, args);
          if (verbose) renderToolCallInline(name, args);
        },
      }
    );

    const { reasoning, content, toolCalls } = flushTurn();

    if (verbose && reasoning) {
      console.log(chalk.gray(reasoning));
    }
    if (verbose) {
      for (const tc of toolCalls) {
        console.log(chalk.blue(`  ⚙ ${tc.name}(${tc.args})`));
      }
    }
    console.log(content);
    console.log();
  } catch (error) {
    console.error(chalk.red(`错误: ${error instanceof Error ? error.message : String(error)}`));
  }
}
