import { readFileSync, existsSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";

export const readFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "read_file",
      description: "读取指定文件的内容，支持指定行偏移和行数限制",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的绝对路径" },
          offset: { type: "integer", description: "起始行号（1-based），默认1" },
          limit: { type: "integer", description: "读取行数，默认2000" },
        },
        required: ["filePath"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const offset = (args.offset as number) || 1;
    const limit = (args.limit as number) || 2000;

    if (!existsSync(filePath)) {
      return `错误：文件不存在 — ${filePath}`;
    }

    const content = readFileSync(filePath, "utf-8");
    const lines = content.split("\n");
    const start = offset - 1;
    const end = Math.min(start + limit, lines.length);
    const selected = lines.slice(start, end);

    const result = selected
      .map((line, i) => `${start + i + 1}: ${line}`)
      .join("\n");

    const header = `文件: ${filePath} (行 ${start + 1}-${end}，共 ${lines.length} 行)\n`;
    return header + result;
  },
};
