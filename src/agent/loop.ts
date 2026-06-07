import { DeepSeekClient } from "../api/client.js";
import type {
  ConversationMessage,
  AgentInput,
  AgentResult,
} from "./types.js";
import { buildSystemPrompt } from "./system-prompt.js";
import { ToolRegistry } from "../tools/index.js";

export interface LoopConfig {
  client: DeepSeekClient;
  softLimit: number;
  hardLimit: number;
  toolRegistry: ToolRegistry;
  mode?: "auto" | "plan";
  initialMessages?: ConversationMessage[];
  onRoundExceeded?: (round: number) => Promise<boolean>;
  onReasoningChunk?: (text: string) => void;
  onContentChunk?: (text: string) => void;
  onToolCall?: (name: string, args: string) => void;
  onToolResult?: (name: string, result: string) => void;
  onRoundUsage?: (usage: { promptTokens: number; completionTokens: number }) => void;
  signal?: AbortSignal;
}


export async function runAgentLoop(
  input: AgentInput,
  config: LoopConfig
): Promise<AgentResult> {
  const systemPrompt = buildSystemPrompt(config.mode ?? "auto");

  let messages: ConversationMessage[];
  if (config.initialMessages?.length) {
    // 替换已有的 system 消息（如果存在），否则在最前面插入
    const sysIdx = config.initialMessages.findIndex((m) => m.role === "system");
    if (sysIdx >= 0) {
      messages = [...config.initialMessages];
      messages[sysIdx] = { role: "system", content: systemPrompt };
    } else {
      messages = [{ role: "system", content: systemPrompt }, ...config.initialMessages];
    }
  } else {
    messages = [{ role: "system", content: systemPrompt }];
  }

  if (input.attachedFiles?.length) {
    for (const file of input.attachedFiles) {
      messages.push({
        role: "user",
        content: `文件 ${file.path} 的内容：\n\`\`\`\n${file.content}\n\`\`\``,
      });
    }
  }

  messages.push({ role: "user", content: input.userMessage });

  let round = 0;
  let hasUsedTools = false;
  const totalUsage = { promptTokens: 0, completionTokens: 0 };
  let lastContent = "";
  let lastReasoning = "";
  let lastError: string | null = null;

  while (round < config.hardLimit) {
    let response;
    try {
      response = await config.client.chat(messages, {
        signal: config.signal,
        tools: config.toolRegistry.getDefinitions(),
        reasoningEffort: hasUsedTools ? "max" : "high",
        onReasoningChunk: config.onReasoningChunk,
        onContentChunk: config.onContentChunk,
        onToolCall: (tc) => config.onToolCall?.(tc.function.name, tc.function.arguments),
      });
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
      break;
    }

    totalUsage.promptTokens += response.usage.promptTokens;
    totalUsage.completionTokens += response.usage.completionTokens;
    config.onRoundUsage?.({
      promptTokens: totalUsage.promptTokens,
      completionTokens: totalUsage.completionTokens,
    });

    messages.push(response.message);

    // 只在非空时保留，避免纯工具调用轮次覆盖之前的有效输出
    if (response.message.content) {
      lastContent = response.message.content;
    }
    if (response.message.reasoning_content) {
      lastReasoning = response.message.reasoning_content;
    }

    if (response.message.tool_calls?.length) {
      hasUsedTools = true;
    }

    if (!response.message.tool_calls?.length) {
      break;
    }

    for (const tc of response.message.tool_calls) {
      const result = await config.toolRegistry.execute(tc);
      config.onToolResult?.(tc.function.name, result);
      messages.push({
        role: "tool",
        tool_call_id: tc.id,
        content: result,
      });
    }

    round++;

    // 已完成 round 轮工具调用，检查是否超软上限
    if (round >= config.softLimit && config.onRoundExceeded) {
      const shouldContinue = await config.onRoundExceeded(round);
      if (!shouldContinue) break;
    }
  }

  if (lastError) {
    lastContent = lastError === "用户中断" ? "已中止。" : `错误：${lastError}`;
  } else if (round >= config.hardLimit) {
    lastContent = `已达到最大轮次 ${config.hardLimit}，任务未完成。`;
  }

  return {
    content: lastContent,
    reasoningContent: lastReasoning || null,
    totalRounds: round,
    usage: totalUsage,
  };
}

