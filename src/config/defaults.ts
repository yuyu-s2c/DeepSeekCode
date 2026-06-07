export const DEFAULT_CONFIG = {
  model: "deepseek-v4-pro",
  baseUrl: "https://api.deepseek.com",
  maxRounds: 50,
  softLimit: 50,
  hardLimit: 200,
  compressThreshold: 0.8,
  maxTokens: 8192,
  excludePatterns: ["node_modules", ".git", "dist", "build", ".next", "__pycache__"],
};
