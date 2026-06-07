export interface ToolCall {
  id: string;
  type: "function";
  function: {
    name: string;
    arguments: string;
  };
}

export interface AssistantMessage {
  role: "assistant";
  content: string | null;
  reasoning_content: string | null;
  tool_calls: ToolCall[] | null;
}

export interface UserMessage {
  role: "user";
  content: string;
}

export interface SystemMessage {
  role: "system";
  content: string;
}

export interface ToolMessage {
  role: "tool";
  tool_call_id: string;
  content: string;
}

export type ConversationMessage =
  | SystemMessage
  | UserMessage
  | AssistantMessage
  | ToolMessage;

export interface ToolDefinition {
  type: "function";
  function: {
    name: string;
    description: string;
    parameters: Record<string, unknown>;
    strict?: boolean;
  };
}

export interface ChatResponse {
  message: AssistantMessage;
  usage: {
    promptTokens: number;
    completionTokens: number;
    cacheHitTokens: number;
    cacheMissTokens: number;
  };
  finishReason: "stop" | "tool_calls" | "length";
}

export interface AgentContext {
  systemPrompt: string;
  messages: ConversationMessage[];
  config: {
    model: string;
    maxRounds: number;
    softLimit: number;
    hardLimit: number;
    maxTokens: number;
  };
  toolDefinitions: ToolDefinition[];
}

export interface AgentInput {
  userMessage: string;
  attachedFiles?: { path: string; content: string }[];
}

export interface AgentResult {
  content: string;
  reasoningContent: string | null;
  totalRounds: number;
  usage: { promptTokens: number; completionTokens: number };
}
