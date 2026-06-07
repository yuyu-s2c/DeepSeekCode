import OpenAI from "openai";
import type { ToolCall, ChatResponse, AssistantMessage, ConversationMessage } from "../agent/types.js";
import type { ToolDefinition } from "../agent/types.js";
import { toOpenAiMessages } from "./message-builder.js";
import { withRetry } from "./retry.js";

export interface ClientConfig {
  apiKey: string;
  baseUrl: string;
  model: string;
  maxTokens: number;
}

export interface ChatOptions {
  tools?: ToolDefinition[];
  signal?: AbortSignal;
  reasoningEffort?: "high" | "max";
  onReasoningChunk?: (text: string) => void;
  onContentChunk?: (text: string) => void;
  onToolCall?: (call: ToolCall) => void;
}

interface DeepSeekDelta {
  content?: string | null;
  reasoning_content?: string;
  tool_calls?: OpenAI.Chat.Completions.ChatCompletionChunk.Choice.Delta.ToolCall[];
}

export class DeepSeekClient {
  private client: OpenAI;
  private config: ClientConfig;

  constructor(config: ClientConfig) {
    this.config = config;
    this.client = new OpenAI({
      apiKey: config.apiKey,
      baseURL: config.baseUrl,
    });
  }

  async chat(
    messages: ConversationMessage[],
    options: ChatOptions = {}
  ): Promise<ChatResponse> {
    return withRetry(async () => {
      const openaiMessages = toOpenAiMessages(messages);

      const stream = await this.client.chat.completions.create({
        model: this.config.model,
        messages: openaiMessages as OpenAI.Chat.Completions.ChatCompletionMessageParam[],
        tools: options.tools?.length
          ? options.tools as OpenAI.Chat.Completions.ChatCompletionTool[]
          : undefined,
        stream: true,
        max_tokens: this.config.maxTokens,
        reasoning_effort: options.reasoningEffort ?? "high",
        extra_body: { thinking: { type: "enabled" } },
        stream_options: { include_usage: true },
      } as unknown as OpenAI.Chat.Completions.ChatCompletionCreateParamsStreaming);

      let reasoningContent = "";
      let content = "";
      const toolCallMap = new Map<number, ToolCall>();
      let lastUsage: OpenAI.CompletionUsage | undefined;
      let lastFinishReason = "stop";

      for await (const chunk of stream) {
        if (options.signal?.aborted) {
          stream.controller.abort();
          throw new Error("用户中断");
        }

        // 最后一帧包含 usage（stream_options: include_usage）
        if (chunk.usage) {
          lastUsage = chunk.usage;
        }
        if (chunk.choices?.[0]?.finish_reason) {
          lastFinishReason = chunk.choices[0].finish_reason;
        }

        const delta = chunk.choices?.[0]?.delta as DeepSeekDelta | undefined;

        if (delta?.reasoning_content) {
          reasoningContent += delta.reasoning_content;
          options.onReasoningChunk?.(delta.reasoning_content);
        }

        if (delta?.content) {
          content += delta.content;
          options.onContentChunk?.(delta.content);
        }

        if (delta?.tool_calls) {
          for (const tcDelta of delta.tool_calls) {
            const idx = tcDelta.index;
            if (!toolCallMap.has(idx)) {
              toolCallMap.set(idx, {
                id: "",
                type: "function",
                function: { name: "", arguments: "" },
              });
            }
            const tc = toolCallMap.get(idx)!;
            if (tcDelta.id) tc.id = tcDelta.id;
            if (tcDelta.function?.name) tc.function.name += tcDelta.function.name;
            if (tcDelta.function?.arguments) tc.function.arguments += tcDelta.function.arguments;
          }
        }
      }

      const toolCalls = toolCallMap.size > 0
        ? Array.from(toolCallMap.values())
        : null;

      if (toolCalls && options.onToolCall) {
        for (const tc of toolCalls) {
          options.onToolCall(tc);
        }
      }

      const finishReason = lastFinishReason as ChatResponse["finishReason"];

      const message: AssistantMessage = {
        role: "assistant",
        content: content || null,
        reasoning_content: reasoningContent || null,
        tool_calls: toolCalls,
      };

      return {
        message,
        usage: {
          promptTokens: lastUsage?.prompt_tokens ?? 0,
          completionTokens: lastUsage?.completion_tokens ?? 0,
          cacheHitTokens: (lastUsage as unknown as Record<string, unknown>)?.prompt_cache_hit_tokens as number ?? 0,
          cacheMissTokens: (lastUsage as unknown as Record<string, unknown>)?.prompt_cache_miss_tokens as number ?? 0,
        },
        finishReason,
      };
    });
  }

  async countTokens(messages: ConversationMessage[]): Promise<number> {
    const openaiMessages = toOpenAiMessages(messages);
    let totalChars = 0;
    for (const msg of openaiMessages) {
      totalChars += JSON.stringify(msg).length;
    }
    return Math.ceil(totalChars / 3);
  }
}
