import type { ConversationMessage } from "../agent/types.js";
import { estimateTokens, MAX_CONTEXT_TOKENS } from "./tokenizer.js";
import { compressHistory } from "./compressor.js";

export class ContextManager {
  private messages: ConversationMessage[];
  private compressThreshold: number;

  constructor(messages: ConversationMessage[] = [], compressThreshold = 0.8) {
    this.messages = messages;
    this.compressThreshold = compressThreshold;
  }

  addMessage(msg: ConversationMessage): void {
    this.messages.push(msg);
  }

  getMessages(): ConversationMessage[] {
    const tokens = estimateTokens(this.messages);
    if (tokens < MAX_CONTEXT_TOKENS * this.compressThreshold) {
      return this.messages;
    }

    const result = compressHistory(this.messages);
    if (result.compressedCount > 0) {
      const systemIndex = this.messages.findIndex((m) => m.role === "system");
      const newMessages = [...this.messages];
      newMessages.splice(systemIndex + 1, 0, result.summaryMessage);
      return newMessages;
    }

    return this.messages;
  }

  reset(): void {
    this.messages = [];
  }

  toJSON(): string {
    return JSON.stringify(this.messages);
  }

  static fromJSON(json: string): ContextManager {
    const messages = JSON.parse(json) as ConversationMessage[];
    return new ContextManager(messages);
  }
}
