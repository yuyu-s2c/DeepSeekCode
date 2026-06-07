const RETRY_CONFIG = {
  maxRetries: 3,
  baseDelay: 1000,
  maxDelay: 30000,
  retryableStatuses: [429, 500, 502, 503],
  retryableErrors: ["rate_limit", "server_error", "busy"],
};

export function isRetryableError(error: unknown): boolean {
  if (error && typeof error === "object" && "status" in error) {
    const status = (error as { status: number }).status;
    if (RETRY_CONFIG.retryableStatuses.includes(status)) return true;
  }
  if (error && typeof error === "object" && "code" in error) {
    const code = (error as { code: string }).code;
    if (RETRY_CONFIG.retryableErrors.includes(code)) return true;
  }
  return false;
}

export function getRetryDelay(attempt: number): number {
  const delay = RETRY_CONFIG.baseDelay * Math.pow(2, attempt);
  return Math.min(delay, RETRY_CONFIG.maxDelay);
}

export async function withRetry<T>(
  fn: () => Promise<T>,
  attempt = 0
): Promise<T> {
  try {
    return await fn();
  } catch (error) {
    if (attempt >= RETRY_CONFIG.maxRetries || !isRetryableError(error)) {
      throw error;
    }
    const delay = getRetryDelay(attempt);
    await new Promise((resolve) => setTimeout(resolve, delay));
    return withRetry(fn, attempt + 1);
  }
}
