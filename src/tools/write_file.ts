import { writeFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
import type { RegisteredTool } from "./registry.js";
import { resolveSafePath } from "./path-utils.js";

export const writeFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "write_file",
      description: "创建新文件或覆盖已有文件的内容",
      strict: true,
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的相对或绝对路径" },
          content: { type: "string", description: "要写入的文件内容" },
        },
        required: ["filePath", "content"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const content = args.content as string;

    const pathResult = resolveSafePath(filePath);
    if (!pathResult.safe) {
      return `错误：${pathResult.error}`;
    }

    try {
      const dir = dirname(pathResult.resolved);
      if (!existsSync(dir)) {
        mkdirSync(dir, { recursive: true });
      }

      const exists = existsSync(pathResult.resolved);
      writeFileSync(pathResult.resolved, content, "utf-8");

      return exists
        ? `文件已覆盖: ${pathResult.resolved}`
        : `文件已创建: ${pathResult.resolved}`;
    } catch (error) {
      return `错误：写入文件失败 — ${error instanceof Error ? error.message : String(error)}`;
    }
  },
};
