import { DeepSeekClient } from "../api/client.js";
import type {
  ConversationMessage,
  AgentInput,
  AgentResult,
  ToolCall,
} from "./types.js";
import { buildSystemPrompt } from "./system-prompt.js";
import type { ToolDefinition } from "./types.js";

export interface LoopConfig {
  client: DeepSeekClient;
  softLimit: number;
  hardLimit: number;
  toolDefinitions: ToolDefinition[];
  onRoundExceeded?: (round: number) => Promise<boolean>;
}


export async function runAgentLoop(
  input: AgentInput,
  config: LoopConfig
): Promise<AgentResult> {
  const systemPrompt = buildSystemPrompt();

  const messages: ConversationMessage[] = [
    { role: "system", content: systemPrompt },
  ];

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
  const totalUsage = { promptTokens: 0, completionTokens: 0 };
  let lastContent = "";
  let lastReasoning = "";
  let lastError: string | null = null;

  while (round < config.hardLimit) {
    let response;
    try {
      response = await config.client.chat(messages, {
        tools: config.toolDefinitions,
      });
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
      break;
    }

    totalUsage.promptTokens += response.usage.promptTokens;
    totalUsage.completionTokens += response.usage.completionTokens;

    messages.push(response.message);

    // 只在非空时保留，避免纯工具调用轮次覆盖之前的有效输出
    if (response.message.content) {
      lastContent = response.message.content;
    }
    if (response.message.reasoning_content) {
      lastReasoning = response.message.reasoning_content;
    }

    if (!response.message.tool_calls?.length) {
      break;
    }

    for (const tc of response.message.tool_calls) {
      const result = await executeToolCall(tc);
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
    lastContent = `错误：${lastError}`;
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

// 占位——Task 4 会让工具注册中心替换
async function executeToolCall(tc: ToolCall): Promise<string> {
  return `工具 ${tc.function.name} 尚未注册，参数: ${tc.function.arguments}`;
}
