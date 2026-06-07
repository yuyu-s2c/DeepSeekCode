import { readFileSync, existsSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";
import { resolveSafePath } from "./path-utils.js";

export const readFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "read_file",
      description: "读取指定文件的内容，支持指定行偏移和行数限制",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的相对或绝对路径" },
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

    const pathResult = resolveSafePath(filePath);
    if (!pathResult.safe) {
      return `错误：${pathResult.error}`;
    }

    if (!existsSync(pathResult.resolved)) {
      return `错误：文件不存在 — ${pathResult.resolved}`;
    }

    const content = readFileSync(pathResult.resolved, "utf-8");
    const lines = content.split("\n");
    const start = Math.max(offset - 1, 0);
    const end = Math.min(start + limit, lines.length);
    const selected = lines.slice(start, end);

    const result = selected
      .map((line, i) => `${start + i + 1}: ${line}`)
      .join("\n");

    const header = `文件: ${pathResult.resolved} (行 ${start + 1}-${end}，共 ${lines.length} 行)\n`;
    return header + result;
  },
};
