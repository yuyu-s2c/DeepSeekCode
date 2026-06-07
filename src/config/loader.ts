import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { DEFAULT_CONFIG } from "./defaults.js";

export interface AppConfig {
  model: string;
  baseUrl: string;
  maxRounds: number;
  softLimit: number;
  hardLimit: number;
  compressThreshold: number;
  maxTokens: number;
  excludePatterns: string[];
}

export function loadConfig(): AppConfig {
  const apiKey = process.env.DEEPSEEK_API_KEY;
  if (!apiKey) {
    console.error("错误：未设置 DEEPSEEK_API_KEY 环境变量");
    process.exit(1);
  }

  let projectConfig: Partial<AppConfig> = {};
  const configPath = resolve(process.cwd(), ".dcode.json");
  if (existsSync(configPath)) {
    try {
      projectConfig = JSON.parse(readFileSync(configPath, "utf-8"));
    } catch {
      console.warn(`警告：.dcode.json 解析失败，使用默认配置`);
    }
  }

  return { ...DEFAULT_CONFIG, ...projectConfig };
}
