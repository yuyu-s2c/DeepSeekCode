import { readdirSync, statSync } from "node:fs";
import { resolve, relative } from "node:path";
import type { RegisteredTool } from "./registry.js";

export const searchFilesTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "search_files",
      description: "按文件名 Glob 模式查找文件，返回匹配的文件路径列表",
      strict: true,
      parameters: {
        type: "object",
        properties: {
          pattern: { type: "string", description: "Glob 模式，如 **/*.ts, src/**/*.tsx" },
          path: {
            type: "string",
            description: "搜索起始目录，传空字符串表示当前目录",
          },
        },
        required: ["pattern", "path"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const pattern = args.pattern as string;
    const startDir = (args.path as string) || process.cwd();

    const results: string[] = [];
    const excludeDirs = new Set([
      "node_modules", ".git", "dist", "build", ".next", "__pycache__",
    ]);

    function globToRegex(glob: string): RegExp {
      const parts = glob.split("/");
      const regexParts = parts.map((part) => {
        if (part === "**") return ".*";
        return part
          .replace(/\./g, "\\.")
          .replace(/\*/g, "[^/]*")
          .replace(/\?/g, "[^/]")
          .replace(/\{([^}]+)\}/g, (_, g) => `(${g.split(",").join("|")})`);
      });
      const regexStr = regexParts.join("/");
      return new RegExp(`^${regexStr}$`);
    }

    let regex: RegExp;
    try {
      regex = globToRegex(pattern);
    } catch {
      return `错误：无效的 Glob 模式 — ${pattern}`;
    }

    function walk(dir: string, basePath: string) {
      let entries: string[];
      try { entries = readdirSync(dir); } catch { return; }

      for (const entry of entries) {
        if (excludeDirs.has(entry)) continue;

        const fullPath = resolve(dir, entry);
        let stat;
        try { stat = statSync(fullPath); } catch { continue; }

        if (stat.isDirectory()) {
          if (pattern.includes("**")) {
            walk(fullPath, basePath);
          }
        } else if (stat.isFile()) {
          const relPath = relative(basePath, fullPath);
          if (regex.test(relPath) || regex.test(entry)) {
            results.push(relPath);
          }
          if (results.length >= 500) return;
        }
      }
    }

    walk(startDir, startDir);

    const header = `匹配 "${pattern}" 的文件 (${results.length} 条):\n`;
    return header + (results.length > 0 ? results.join("\n") : "(无结果)");
  },
};
