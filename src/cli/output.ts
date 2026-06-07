// 轻量输出缓冲区，供 agent/loop.ts 流式回调使用

import chalk from "chalk";

let bufferedReasoning = "";
let bufferedContent = "";
let currentToolCalls: Array<{ name: string; args: string }> = [];

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

export function flushTurn(): {
  reasoning: string;
  content: string;
  toolCalls: Array<{ name: string; args: string }>;
} {
  const result = {
    reasoning: bufferedReasoning,
    content: bufferedContent,
    toolCalls: [...currentToolCalls],
  };
  bufferedReasoning = "";
  bufferedContent = "";
  currentToolCalls = [];
  return result;
}

export function renderReasoningChunk(text: string): void {
  process.stdout.write(chalk.gray(text));
}

export function renderContentChunk(text: string): void {
  process.stdout.write(text);
}

export function renderToolCallInline(name: string, args: string): void {
  const shortArgs = args.length > 70 ? args.slice(0, 67) + "..." : args;
  process.stdout.write(chalk.blue(`\n  ⚙ ${name}(${shortArgs})`));
}
