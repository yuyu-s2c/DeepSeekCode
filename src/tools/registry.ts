import type { ToolDefinition, ToolCall } from "../agent/types.js";

export interface RegisteredTool {
  definition: ToolDefinition;
  execute: (args: Record<string, unknown>) => Promise<string>;
}

export class ToolRegistry {
  private tools = new Map<string, RegisteredTool>();

  register(tool: RegisteredTool): void {
    this.tools.set(tool.definition.function.name, tool);
  }

  getDefinitions(): ToolDefinition[] {
    return Array.from(this.tools.values()).map((t) => t.definition);
  }

  async execute(toolCall: ToolCall): Promise<string> {
    const tool = this.tools.get(toolCall.function.name);
    if (!tool) {
      return `错误：未知工具 ${toolCall.function.name}`;
    }
    try {
      const args = JSON.parse(toolCall.function.arguments);
      return await tool.execute(args);
    } catch (error) {
      return `错误：执行工具 ${toolCall.function.name} 失败 — ${error instanceof Error ? error.message : String(error)}`;
    }
  }
}
