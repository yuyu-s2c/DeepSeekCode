import type { ToolDefinition, ToolCall } from "../agent/types.js";

export interface RegisteredTool {
  definition: ToolDefinition;
  execute: (args: Record<string, unknown>) => Promise<string>;
}

export class ToolRegistry {
  private tools = new Map<string, RegisteredTool>();
  private blockedTools = new Set<string>();

  register(tool: RegisteredTool): void {
    this.tools.set(tool.definition.function.name, tool);
  }

  setPlanMode(enabled: boolean): void {
    if (enabled) {
      // Plan Mode: 屏蔽写入和执行类工具，保留只读工具
      this.blockedTools.add("write_file");
      this.blockedTools.add("edit_file");
      this.blockedTools.add("run_shell");
    } else {
      this.blockedTools.clear();
    }
  }

  getDefinitions(): ToolDefinition[] {
    return Array.from(this.tools.values())
      .filter((t) => !this.blockedTools.has(t.definition.function.name))
      .map((t) => t.definition);
  }

  async execute(toolCall: ToolCall): Promise<string> {
    if (this.blockedTools.has(toolCall.function.name)) {
      return "Plan Mode: 此工具已禁用，请切换到 Auto Mode 后重试。";
    }
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
