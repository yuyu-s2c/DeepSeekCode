import type { ConversationMessage } from "../agent/types.js";

export function estimateTokens(messages: ConversationMessage[]): number {
  let totalChars = 0;
  for (const msg of messages) {
    if (msg.role === "system") {
      totalChars += msg.content.length;
    } else if (msg.role === "user") {
      totalChars += msg.content.length;
    } else if (msg.role === "tool") {
      totalChars += msg.content.length;
    } else if (msg.role === "assistant") {
      totalChars += (msg.content ?? "").length;
      totalChars += (msg.reasoning_content ?? "").length;
      if (msg.tool_calls) {
        totalChars += JSON.stringify(msg.tool_calls).length;
      }
    }
  }
  return Math.ceil(totalChars / 3);
}

// DeepSeek V4 Pro 上下文窗口约 128K tokens
export const MAX_CONTEXT_TOKENS = 120_000;
