import { readFileSync, writeFileSync, existsSync } from "node:fs";
import type { RegisteredTool } from "./registry.js";
import { resolveSafePath } from "./path-utils.js";

export const editFileTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "edit_file",
      description:
        "精确替换文件中的指定字符串。oldString 必须在文件中唯一匹配。",
      strict: true,
      parameters: {
        type: "object",
        properties: {
          filePath: { type: "string", description: "文件的相对或绝对路径" },
          oldString: { type: "string", description: "要被替换的原始字符串" },
          newString: { type: "string", description: "替换后的新字符串" },
        },
        required: ["filePath", "oldString", "newString"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const filePath = args.filePath as string;
    const oldString = args.oldString as string;
    const newString = args.newString as string;

    const pathResult = resolveSafePath(filePath);
    if (!pathResult.safe) {
      return `错误：${pathResult.error}`;
    }

    if (!existsSync(pathResult.resolved)) {
      return `错误：文件不存在 — ${pathResult.resolved}`;
    }

    const original = readFileSync(pathResult.resolved, "utf-8");
    const count = original.split(oldString).length - 1;

    if (count === 0) {
      return `错误：未找到匹配的字符串 — 文件中不存在指定的 oldString`;
    }
    if (count > 1) {
      return `错误：找到 ${count} 处匹配 — oldString 必须唯一，请提供更多上下文`;
    }

    const backup = original;
    try {
      const modified = original.replace(oldString, newString);
      writeFileSync(pathResult.resolved, modified, "utf-8");

      const oldLines = oldString.split("\n");
      const newLines = newString.split("\n");
      const diffLines: string[] = [];
      const maxLen = Math.max(oldLines.length, newLines.length);
      for (let i = 0; i < maxLen; i++) {
        if (i < oldLines.length && i < newLines.length) {
          if (oldLines[i] !== newLines[i]) {
            diffLines.push(`- ${oldLines[i]}`);
            diffLines.push(`+ ${newLines[i]}`);
          }
        } else if (i < oldLines.length) {
          diffLines.push(`- ${oldLines[i]}`);
        } else {
          diffLines.push(`+ ${newLines[i]}`);
        }
      }

      return `编辑成功: ${pathResult.resolved}\n\`\`\`diff\n${diffLines.join("\n")}\n\`\`\``;
    } catch (error) {
      writeFileSync(pathResult.resolved, backup, "utf-8");
      return `错误：编辑失败，已回滚 — ${error instanceof Error ? error.message : String(error)}`;
    }
  },
};
