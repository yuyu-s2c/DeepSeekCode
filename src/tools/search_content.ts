import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { resolve, relative } from "node:path";
import type { RegisteredTool } from "./registry.js";

const MAX_RESULTS = 500;

export const searchContentTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "search_content",
      description: "在文件中搜索匹配正则表达式的内容，返回文件路径和行号",
      strict: true,
      parameters: {
        type: "object",
        properties: {
          pattern: { type: "string", description: "正则表达式搜索模式" },
          path: {
            type: "string",
            description: "搜索目录路径，传空字符串表示当前目录",
          },
          include: {
            type: "string",
            description: "文件类型过滤（如 *.ts, *.{js,ts}），传空字符串或 * 表示所有文件",
          },
        },
        required: ["pattern", "path", "include"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const pattern = args.pattern as string;
    const searchDir = (args.path as string) || process.cwd();
    const include = (args.include as string) || "*";

    let regex: RegExp;
    try {
      regex = new RegExp(pattern, "g");
    } catch {
      return `错误：无效的正则表达式 — ${pattern}`;
    }

    const absDir = resolve(searchDir);
    const excludeDirs = new Set([
      "node_modules", ".git", "dist", "build", ".next", "__pycache__",
    ]);

    const results: string[] = [];
    let count = 0;

    function matchGlob(filename: string, globPattern: string): boolean {
      if (globPattern === "*") return true;
      const regexStr = globPattern
        .replace(/\./g, "\\.")
        .replace(/\*/g, ".*")
        .replace(/\{([^}]+)\}/g, (_, g) => `(${g.split(",").join("|")})`);
      return new RegExp(`^${regexStr}$`).test(filename);
    }

    function walk(dir: string) {
      let entries: string[];
      try { entries = readdirSync(dir); } catch { return; }

      for (const entry of entries) {
        if (count >= MAX_RESULTS) return;
        if (excludeDirs.has(entry)) continue;

        const fullPath = resolve(dir, entry);
        let stat;
        try { stat = statSync(fullPath); } catch { continue; }

        if (stat.isDirectory()) {
          walk(fullPath);
        } else if (stat.isFile()) {
          if (!matchGlob(entry, include)) continue;
          try {
            const content = readFileSync(fullPath, "utf-8");
            const lines = content.split("\n");
            for (let i = 0; i < lines.length; i++) {
              regex.lastIndex = 0;
              if (regex.test(lines[i])) {
                const relPath = relative(process.cwd(), fullPath);
                results.push(`${relPath}:${i + 1}: ${lines[i].trim().substring(0, 200)}`);
                count++;
                if (count >= MAX_RESULTS) break;
              }
            }
          } catch { /* skip unreadable */ }
        }
      }
    }

    walk(absDir);

    const header = `搜索 "${pattern}" 的结果 (${count} 条，上限 ${MAX_RESULTS}):\n`;
    return header + (results.length > 0 ? results.join("\n") : "(无结果)");
  },
};
