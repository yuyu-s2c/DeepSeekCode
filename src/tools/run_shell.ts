import { execSync } from "node:child_process";
import type { RegisteredTool } from "./registry.js";

let approvalCallback: ((command: string) => Promise<boolean>) | null = null;

const DANGEROUS_PATTERNS = [
  /rm\s+-rf\s+\//i,
  /del\s+\/f\s+\/s\s+[A-Z]:\\/i,
  /format\s+[A-Z]:/i,
  />\s*\/dev\/sd[a-z]/i,
  /mkfs/i,
  /dd\s+if=/i,
];

function isDangerous(command: string): boolean {
  return DANGEROUS_PATTERNS.some((p) => p.test(command));
}

export function setApprovalCallback(cb: (command: string) => Promise<boolean>): void {
  approvalCallback = cb;
}

export const runShellTool: RegisteredTool = {
  definition: {
    type: "function",
    function: {
      name: "run_shell",
      description: "在终端中执行 Shell 命令。危险操作需要用户确认。",
      strict: true,
      parameters: {
        type: "object",
        properties: {
          command: { type: "string", description: "要执行的 Shell 命令" },
          workdir: {
            type: "string",
            description: "工作目录，传空字符串表示当前目录",
          },
          timeout: {
            type: "integer",
            description: "超时时间（毫秒），传 0 表示默认 120000",
          },
        },
        required: ["command", "workdir", "timeout"],
        additionalProperties: false,
      },
    },
  },
  execute: async (args) => {
    const command = args.command as string;
    const workdir = (args.workdir as string) || process.cwd();
    const timeout = (args.timeout as number) > 0 ? (args.timeout as number) : 120000;

    if (isDangerous(command)) {
      return `错误：拒绝执行危险命令 — ${command}`;
    }

    if (!approvalCallback) {
      return "错误：Shell 执行未配置审批回调，命令被拒绝";
    }

    const approved = await approvalCallback(command);
    if (!approved) {
      return "用户取消了命令执行";
    }

    try {
      const output = execSync(command, {
        cwd: workdir,
        timeout,
        encoding: "utf-8",
        maxBuffer: 10 * 1024 * 1024,
      });
      return output || "(命令执行成功，无输出)";
    } catch (error: unknown) {
      if (error && typeof error === "object" && "stdout" in error) {
        const execError = error as { stdout: string; stderr: string; message: string };
        return `命令执行失败:\nSTDOUT:\n${execError.stdout || "(无)"}\nSTDERR:\n${execError.stderr || "(无)"}\n${execError.message}`;
      }
      return `命令执行失败: ${error instanceof Error ? error.message : String(error)}`;
    }
  },
};
