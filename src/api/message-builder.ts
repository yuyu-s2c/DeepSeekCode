import type { ConversationMessage } from "../agent/types.js";

interface OpenAIMessage {
  role: string;
  content?: string | null;
  reasoning_content?: string | null;
  tool_calls?: unknown;
  tool_call_id?: string;
}

export function toOpenAiMessages(
  messages: ConversationMessage[]
): OpenAIMessage[] {
  return messages.map((msg) => {
    switch (msg.role) {
      case "system":
        return { role: "system", content: msg.content };
      case "user":
        return { role: "user", content: msg.content };
      case "tool":
        return {
          role: "tool",
          tool_call_id: msg.tool_call_id,
          content: msg.content,
        };
      case "assistant": {
        const result: OpenAIMessage = {
          role: "assistant",
          content: msg.content,
          reasoning_content: msg.reasoning_content,
        };
        if (msg.tool_calls) {
          result.tool_calls = msg.tool_calls;
        }
        return result;
      }
    }
  });
}
