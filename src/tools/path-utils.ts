import { resolve } from "node:path";

let workspaceRoot = process.cwd();

export function setWorkspaceRoot(root: string): void {
  workspaceRoot = resolve(root);
}

export function getWorkspaceRoot(): string {
  return workspaceRoot;
}

export function resolveSafePath(filePath: string): { safe: true; resolved: string } | { safe: false; error: string } {
  const resolved = resolve(filePath);
  const normalizedRoot = resolve(workspaceRoot);

  if (!resolved.startsWith(normalizedRoot + "\\") && resolved !== normalizedRoot) {
    return {
      safe: false,
      error: `路径超出工作区范围: ${filePath}。仅允许在 ${workspaceRoot} 内操作。`,
    };
  }

  return { safe: true, resolved };
}
