import { writeFileSync, existsSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";
import type { RegisteredTool } from "./registry.js";

export const writeFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "write_file",
      description: "创建新文件或覆盖已有文件的内容",
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的绝对路径" },
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

    const dir = dirname(filePath);
    if (!existsSync(dir)) {
      mkdirSync(dir, { recursive: true });
    }

    const exists = existsSync(filePath);
    writeFileSync(filePath, content, "utf-8");

    return exists
      ? `文件已覆盖: ${filePath}`
      : `文件已创建: ${filePath}`;
  },
};
