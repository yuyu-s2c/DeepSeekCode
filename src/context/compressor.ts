import type { ConversationMessage } from "../agent/types.js";

export interface CompressionResult {
  summaryMessage: ConversationMessage;
  compressedCount: number;
}

export function compressHistory(
  messages: ConversationMessage[],
  keepRecent: number = 5
): CompressionResult {
  const systemIndex = messages.findIndex((m) => m.role === "system");
  if (systemIndex === -1) {
    return {
      summaryMessage: { role: "user", content: "(无历史)" },
      compressedCount: 0,
    };
  }

  const endIndex = messages.length - keepRecent;
  if (endIndex <= systemIndex + 1) {
    return {
      summaryMessage: { role: "user", content: "(对话历史较短，无需压缩)" },
      compressedCount: 0,
    };
  }

  const toCompress = messages.slice(systemIndex + 1, endIndex);
  const summaryItems: string[] = [];

  for (const msg of toCompress) {
    switch (msg.role) {
      case "user":
        summaryItems.push(`用户: ${msg.content.substring(0, 100)}`);
        break;
      case "assistant": {
        const text = msg.content ?? msg.reasoning_content ?? "";
        if (text) {
          summaryItems.push(`助手: ${text.substring(0, 200)}`);
        }
        if (msg.tool_calls) {
          const toolNames = msg.tool_calls.map((tc) => tc.function.name).join(", ");
          summaryItems.push(`  → 调用工具: ${toolNames}`);
        }
        break;
      }
      case "tool":
        summaryItems.push(`  → 工具结果: ${msg.content.substring(0, 100)}`);
        break;
    }
  }

  const summary = [
    `以下是与 DeepSeek 的历史对话摘要（共 ${toCompress.length} 条消息）：`,
    ...summaryItems,
    "--- 以上为历史摘要，以下为最近对话 ---",
  ].join("\n");

  return {
    summaryMessage: { role: "user", content: summary },
    compressedCount: toCompress.length,
  };
}
