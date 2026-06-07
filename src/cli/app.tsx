import React, { useState, useRef, useCallback, useEffect } from "react";
import { Box, Text, useStdout } from "ink";
import * as readline from "node:readline";
import { loadConfig } from "../config/loader.js";
import { DeepSeekClient } from "../api/client.js";
import { runAgentLoop } from "../agent/loop.js";
import { ContextManager } from "../context/manager.js";
import {
  ToolRegistry,
  readFileTool,
  writeFileTool,
  editFileTool,
  runShellTool,
  setApprovalCallback,
  searchContentTool,
  searchFilesTool,
} from "../tools/index.js";
import ContentArea from "./components/ContentArea.js";

interface ChatMessage {
  role: "user" | "assistant" | "reasoning" | "tool";
  content: string;
}

interface AppProps {
  verbose: boolean;
  initialMessage?: string;
}

interface InputState {
  buffer: string;
  cursor: number;
  history: string[];
  historyIdx: number;
  savedBuffer: string;
}

const MAX_CONTEXT = 1_000_000;

const SPINNER = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

function formatTokens(n: number): string {
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds}s`;
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${s}s`;
}

export default function App({ verbose, initialMessage }: AppProps) {
  // ── 对话状态 ──
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [streamingContent, setStreamingContent] = useState("");
  const [streamingReasoning, setStreamingReasoning] = useState("");
  const [status, setStatus] = useState<"ready" | "thinking">("ready");
  const [showReasoning, setShowReasoning] = useState(verbose);
  const [spinnerFrame, setSpinnerFrame] = useState(0);

  // 模式切换
  const [mode, setMode] = useState<"auto" | "plan">("auto");

  const [thinkingElapsed, setThinkingElapsed] = useState(0);
  const [thinkingTokens, setThinkingTokens] = useState(0);

  // 工具执行状态
  const [executingTool, setExecutingTool] = useState<string | null>(null);
  const isToolPhaseRef = useRef(false);

  const [doneElapsed, setDoneElapsed] = useState(0);
  const [doneRounds, setDoneRounds] = useState(0);
  const [donePrompt, setDonePrompt] = useState(0);
  const [doneCompletion, setDoneCompletion] = useState(0);

  const [totalPrompt, setTotalPrompt] = useState(0);
  const [totalCompletion, setTotalCompletion] = useState(0);

  // ── 输入状态 ──
  const [inputState, setInputState] = useState<InputState>({
    buffer: "",
    cursor: 0,
    history: [],
    historyIdx: -1,
    savedBuffer: "",
  });

  const { stdout } = useStdout();
  const rows = stdout?.rows ?? 24;
  const columns = stdout?.columns ?? 80;

  const configRef = useRef(loadConfig());
  const clientRef = useRef<DeepSeekClient>();
  const registryRef = useRef<ToolRegistry>();
  const contextRef = useRef<ContextManager>();
  const streamingContentRef = useRef("");
  const streamingReasoningRef = useRef("");
  const thinkingStartRef = useRef(0);
  const statusRef = useRef(status);
  const inputStateRef = useRef(inputState);
  const sendMessageRef = useRef<(text: string) => void>();

  // 同步 refs
  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  useEffect(() => {
    inputStateRef.current = inputState;
  }, [inputState]);

  // ── 粘贴处理 ──
  const abortRef = useRef<AbortController | null>(null);
  const [isPastedInput, setIsPastedInput] = useState(false);
  const pasteModeRef = useRef(false);
  const pasteBufRef = useRef("");
  const pasteNewlineRef = useRef(false);
  const lastKeyTimeRef = useRef(0);

  // ── 思考动画 + 计时 ──
  useEffect(() => {
    if (status === "thinking") {
      const timer = setInterval(() => {
        setSpinnerFrame((f) => (f + 1) % 10);
        setThinkingElapsed(
          Math.floor((Date.now() - thinkingStartRef.current) / 1000)
        );
      }, 100);
      return () => clearInterval(timer);
    }
  }, [status]);

  // ── 初始化 Agent ──
  useEffect(() => {
    const config = configRef.current;
    const registry = new ToolRegistry();
    registry.register(readFileTool);
    registry.register(writeFileTool);
    registry.register(editFileTool);
    registry.register(runShellTool);
    registry.register(searchContentTool);
    registry.register(searchFilesTool);
    registryRef.current = registry;

    const client = new DeepSeekClient({
      apiKey: config.apiKey,
      baseUrl: config.baseUrl,
      model: config.model,
      maxTokens: config.maxTokens,
      strict: true,
    });
    clientRef.current = client;

    contextRef.current = new ContextManager();

    if (initialMessage) {
      sendMessage(initialMessage);
    }
  }, []);

  // 模式变化时更新工具审批回调 + registry 只读限制
  useEffect(() => {
    setApprovalCallback(async () => mode === "auto");
    registryRef.current?.setPlanMode(mode === "plan");
  }, [mode]);

  // ── 键盘输入处理 ──
  useEffect(() => {
    if (!process.stdin.isTTY) return;

    readline.emitKeypressEvents(process.stdin);
    if (process.stdin.isTTY) {
      process.stdin.setRawMode(true);
    }

    const handler = (
      str: string | undefined,
      key: readline.Key
    ) => {
      const isThinking = statusRef.current === "thinking";

      // 粘贴间隔检测：区分手动回车（>40ms 间隔）与粘贴中的 \r\n
      const now = Date.now();
      const timeSinceLastKey = now - lastKeyTimeRef.current;
      lastKeyTimeRef.current = now;

      // 跳过粘贴 \r\n 中的 \n（已在 \r 处理时插入换行）
      if (pasteNewlineRef.current) {
        pasteNewlineRef.current = false;
        if (
          key.name === "enter" ||
          key.name === "return" ||
          (key.ctrl && key.name === "j") ||
          (typeof str === "string" && str === "\n")
        ) {
          return;
        }
      }

      // Bracketed paste 开始
      if (key.sequence === "\x1b[200~") {
        pasteModeRef.current = true;
        pasteBufRef.current = "";
        return;
      }

      // Bracketed paste 结束
      if (key.sequence === "\x1b[201~" && pasteModeRef.current) {
        pasteModeRef.current = false;
        const rawText = pasteBufRef.current;
        pasteBufRef.current = "";
        if (rawText && !isThinking) {
          // 归一化换行：\r\n → \n, \r → \n
          const text = rawText.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
          setInputState((prev) => ({
            ...prev,
            buffer:
              prev.buffer.slice(0, prev.cursor) +
              text +
              prev.buffer.slice(prev.cursor),
            cursor: prev.cursor + text.length,
          }));
          setIsPastedInput(true);
        }
        return;
      }

      // 粘贴模式中：积累文本（str 对控制字符为 undefined，用 sequence 兜底）
      if (pasteModeRef.current) {
        pasteBufRef.current += str ?? key.sequence ?? "";
        return;
      }

      // Ctrl+C — 思考中中止，否则清空输入
      if (key.ctrl && key.name === "c") {
        if (isThinking) {
          abortRef.current?.abort();
          return;
        }
        setInputState((prev) => ({
          ...prev,
          buffer: "",
          cursor: 0,
        }));
        return;
      }

      // Ctrl+D 空行退出
      if (key.ctrl && key.name === "d") {
        if (!inputStateRef.current.buffer) {
          process.exit(0);
        }
        return;
      }

      // 思考中忽略输入
      if (isThinking) return;

      // Enter / Return — 提交（粘贴中的 \r\n 插入换行）
      if (key.name === "return" || key.name === "enter") {
        // 粘贴检测：按键间隔 < 40ms 说明是粘贴中的换行符（含同 tick 事件 timeSinceLastKey=0）
        if (timeSinceLastKey < 40) {
          setInputState((prev) => ({
            ...prev,
            buffer:
              prev.buffer.slice(0, prev.cursor) +
              "\n" +
              prev.buffer.slice(prev.cursor),
            cursor: prev.cursor + 1,
          }));
          pasteNewlineRef.current = true;
          setIsPastedInput(true);
          return;
        }
        setInputState((prev) => {
          if (!prev.buffer.trim()) return prev;
          const buffer = prev.buffer;
          setIsPastedInput(false);
          const next: InputState = {
            buffer: "",
            cursor: 0,
            history: [...prev.history, buffer],
            historyIdx: -1,
            savedBuffer: "",
          };
          inputStateRef.current = next;
          queueMicrotask(() => sendMessageRef.current?.(buffer));
          return next;
        });
        return;
      }

      // Ctrl+J — 插入换行
      if (key.ctrl && key.name === "j") {
        setInputState((prev) => ({
          ...prev,
          buffer:
            prev.buffer.slice(0, prev.cursor) +
            "\n" +
            prev.buffer.slice(prev.cursor),
          cursor: prev.cursor + 1,
        }));
        return;
      }

      // Backspace
      if (key.name === "backspace") {
        setInputState((prev) => {
          if (prev.cursor === 0) return prev;
          const newBuffer =
            prev.buffer.slice(0, prev.cursor - 1) +
            prev.buffer.slice(prev.cursor);
          return { ...prev, buffer: newBuffer, cursor: prev.cursor - 1 };
        });
        return;
      }

      // Delete
      if (key.name === "delete") {
        setInputState((prev) => {
          if (prev.cursor >= prev.buffer.length) return prev;
          return {
            ...prev,
            buffer:
              prev.buffer.slice(0, prev.cursor) +
              prev.buffer.slice(prev.cursor + 1),
          };
        });
        return;
      }

      // 方向键
      if (key.name === "up") {
        setInputState((prev) => {
          if (prev.history.length === 0) return prev;
          const newIdx =
            prev.historyIdx === -1
              ? prev.history.length - 1
              : Math.max(0, prev.historyIdx - 1);
          const saved =
            prev.historyIdx === -1 ? prev.buffer : prev.savedBuffer;
          return {
            ...prev,
            buffer: prev.history[newIdx],
            cursor: prev.history[newIdx].length,
            historyIdx: newIdx,
            savedBuffer: saved,
          };
        });
        return;
      }

      if (key.name === "down") {
        setInputState((prev) => {
          if (prev.historyIdx === -1) return prev;
          const newIdx = prev.historyIdx + 1;
          if (newIdx >= prev.history.length) {
            return {
              ...prev,
              buffer: prev.savedBuffer,
              cursor: prev.savedBuffer.length,
              historyIdx: -1,
            };
          }
          return {
            ...prev,
            buffer: prev.history[newIdx],
            cursor: prev.history[newIdx].length,
            historyIdx: newIdx,
          };
        });
        return;
      }

      if (key.name === "left") {
        setInputState((prev) => ({
          ...prev,
          cursor: Math.max(prev.cursor - 1, 0),
        }));
        return;
      }

      if (key.name === "right") {
        setInputState((prev) => ({
          ...prev,
          cursor: Math.min(prev.cursor + 1, prev.buffer.length),
        }));
        return;
      }

      // Home — 行首
      if (key.name === "home") {
        setInputState((prev) => {
          const before = prev.buffer.slice(0, prev.cursor);
          const lineStart = before.lastIndexOf("\n") + 1;
          return { ...prev, cursor: lineStart };
        });
        return;
      }

      // End — 行尾
      if (key.name === "end") {
        setInputState((prev) => {
          const after = prev.buffer.slice(prev.cursor);
          const lineEnd = after.indexOf("\n");
          return {
            ...prev,
            cursor:
              prev.cursor +
              (lineEnd === -1 ? after.length : lineEnd),
          };
        });
        return;
      }

      // Ctrl+W
      if (key.ctrl && key.name === "w") {
        setInputState((prev) => {
          const before = prev.buffer.slice(0, prev.cursor);
          const cutPos =
            before.search(/\S+$/) === -1 ? 0 : before.search(/\S+$/);
          return {
            ...prev,
            buffer:
              prev.buffer.slice(0, cutPos) +
              prev.buffer.slice(prev.cursor),
            cursor: cutPos,
          };
        });
        return;
      }

      // Ctrl+A
      if (key.ctrl && key.name === "a") {
        setInputState((prev) => ({ ...prev, cursor: 0 }));
        return;
      }

      // Ctrl+E
      if (key.ctrl && key.name === "e") {
        setInputState((prev) => ({
          ...prev,
          cursor: prev.buffer.length,
        }));
        return;
      }

      // Ctrl+K
      if (key.ctrl && key.name === "k") {
        setInputState((prev) => ({
          ...prev,
          buffer: prev.buffer.slice(0, prev.cursor),
        }));
        return;
      }

      // Ctrl+U
      if (key.ctrl && key.name === "u") {
        setInputState((prev) => ({
          ...prev,
          buffer: prev.buffer.slice(prev.cursor),
          cursor: 0,
        }));
        return;
      }

      // Tab
      // 模式切换
      if (key.name === "tab") {
        if (isThinking) return;
        setMode((prev) => (prev === "auto" ? "plan" : "auto"));
        return;
      }

      // 普通字符 / 中文
      if (str && str.length > 0) {
        setInputState((prev) => ({
          ...prev,
          buffer:
            prev.buffer.slice(0, prev.cursor) +
            str +
            prev.buffer.slice(prev.cursor),
          cursor:
            prev.cursor + (str.length > 1 ? str.length : 1),
        }));
        return;
      }
    };

    process.stdin.on("keypress", handler);

    return () => {
      process.stdin.off("keypress", handler);
      if (process.stdin.isTTY) {
        process.stdin.setRawMode(false);
      }
    };
  }, []);

  // ── 命令处理 ──
  const handleCommand = useCallback((text: string) => {
    switch (text) {
      case "/help":
        setMessages((prev) => [
          ...prev,
          {
            role: "assistant",
            content: "命令: /help /quit /clear /verbose",
          },
        ]);
        break;
      case "/quit":
        process.exit(0);
      case "/clear":
        contextRef.current?.reset();
        setMessages([]);
        setTotalPrompt(0);
        setTotalCompletion(0);
        setDoneElapsed(0);
        break;
      case "/verbose":
        setShowReasoning((prev) => !prev);
        setMessages((prev) => [
          ...prev,
          {
            role: "assistant",
            content: `思维链显示: ${!showReasoning ? "开启" : "关闭"}`,
          },
        ]);
        break;
      default:
        setMessages((prev) => [
          ...prev,
          { role: "assistant", content: `未知命令: ${text}` },
        ]);
    }
  }, [showReasoning]);

  // ── 发送消息 ──
  const sendMessage = useCallback(
    async (text: string) => {
      const client = clientRef.current;
      const registry = registryRef.current;
      const context = contextRef.current;
      if (!client || !registry || !context) return;

      // 命令处理
      if (text.startsWith("/")) {
        handleCommand(text);
        return;
      }

      setMessages((prev) => [...prev, { role: "user", content: text }]);
      setStatus("thinking");
      setStreamingContent("");
      setStreamingReasoning("");
      streamingContentRef.current = "";
      streamingReasoningRef.current = "";
      setThinkingTokens(0);
      thinkingStartRef.current = Date.now();
      setThinkingElapsed(0);
      setDoneElapsed(0);

      // Plan Mode: 系统提示词已处理，无需额外前缀
      const initialMessages = context.getMessages();

      // 创建本轮 AbortController
      const controller = new AbortController();
      abortRef.current = controller;

      try {
        const result = await runAgentLoop(
          { userMessage: text },
          {
            client,
            softLimit: configRef.current.softLimit,
            hardLimit: configRef.current.hardLimit,
            toolRegistry: registry,
            mode,
            initialMessages,
            signal: controller.signal,
            onReasoningChunk: (chunk) => {
              if (isToolPhaseRef.current) {
                isToolPhaseRef.current = false;
                setExecutingTool(null);
              }
              streamingReasoningRef.current += chunk;
              setStreamingReasoning(streamingReasoningRef.current);
              setThinkingTokens(
                (prev) => prev + Math.max(1, Math.ceil(chunk.length / 3))
              );
            },
            onContentChunk: (chunk) => {
              if (isToolPhaseRef.current) {
                isToolPhaseRef.current = false;
                setExecutingTool(null);
              }
              streamingContentRef.current += chunk;
              setStreamingContent(streamingContentRef.current);
              setThinkingTokens(
                (prev) => prev + Math.max(1, Math.ceil(chunk.length / 3))
              );
            },
            onToolCall: (name, args) => {
              const partial = streamingContentRef.current;
              if (partial) {
                setMessages((prev) => [
                  ...prev,
                  { role: "assistant", content: partial },
                ]);
                streamingContentRef.current = "";
                setStreamingContent("");
              }
              if (showReasoning) {
                const partialR = streamingReasoningRef.current;
                if (partialR) {
                  setMessages((prev) => [
                    ...prev,
                    { role: "reasoning", content: partialR },
                  ]);
                  streamingReasoningRef.current = "";
                  setStreamingReasoning("");
                }
              }
              const shortArgs =
                args.length > 50 ? args.slice(0, 47) + "..." : args;
              setMessages((prev) => [
                ...prev,
                { role: "tool", content: `${name}(${shortArgs})` },
              ]);
            },
            onToolResult: (name) => {
              setExecutingTool(name);
              isToolPhaseRef.current = true;
            },
            onRoundExceeded: async () => false,
            onRoundUsage: (usage) => {
              setTotalPrompt(usage.promptTokens);
              setTotalCompletion(usage.completionTokens);
            },
          }
        );

        const finalContent = streamingContentRef.current || result.content;
        if (finalContent) {
          setMessages((prev) => [
            ...prev,
            { role: "assistant", content: finalContent },
          ]);
        }

        context.addMessage({ role: "user", content: text });
        context.addMessage({
          role: "assistant",
          content: finalContent,
          reasoning_content: result.reasoningContent,
          tool_calls: null,
        });

        setStreamingContent("");
        setStreamingReasoning("");
        streamingContentRef.current = "";
        streamingReasoningRef.current = "";
        setStatus("ready");

        const elapsed = Math.floor(
          (Date.now() - thinkingStartRef.current) / 1000
        );
        setDoneElapsed(elapsed);
        setDoneRounds(result.totalRounds);
        setDonePrompt(result.usage.promptTokens);
        setDoneCompletion(result.usage.completionTokens);
        // 累计 token 由 onRoundUsage 回调实时更新，无需额外累加
        abortRef.current = null;
      } catch (error) {
        abortRef.current = null;
        const errMsg =
          error instanceof Error ? error.message : String(error);
        setMessages((prev) => [
          ...prev,
          { role: "assistant", content: `错误: ${errMsg}` },
        ]);
        setStreamingContent("");
        setStreamingReasoning("");
        streamingContentRef.current = "";
        streamingReasoningRef.current = "";
        setStatus("ready");
        setDoneElapsed(0);
      }
    },
    [showReasoning, handleCommand, mode]
  );

  // 同步 sendMessage 和 handleCommand 到 ref
  useEffect(() => {
    sendMessageRef.current = sendMessage;
  }, [sendMessage]);

  const { buffer, cursor: cur } = inputState;

  // ── 布局 ──
  // 动态计算内容区可用行数
  //   outerHeight = rows - 1, border 占 2 行 → 内部高度 = rows - 3
  //   底部固定: 分隔线*2 + 模式行 + ctx行 = 4
  const innerHeight = Math.max((rows ?? 24) - 3, 7);

  const MAX_INPUT_LINES = 5;
  const bufferLineCount = status === "ready"
    ? Math.max(buffer.split("\n").length, 1)
    : 0;
  const truncateInput = isPastedInput && bufferLineCount > MAX_INPUT_LINES;
  const effectiveInputLines = truncateInput ? 1 : bufferLineCount;

  let indicatorLines = 0;
  if (status === "thinking") indicatorLines = 2;
  else if (status === "ready" && doneElapsed > 0) indicatorLines = 1;
  const contentLines = Math.max(
    innerHeight - 4 - effectiveInputLines - indicatorLines,
    4
  );
  const sep = "─".repeat(Math.max(columns - 4, 40));
  const ctxTokens = totalPrompt + totalCompletion;
  const ctxPercent = Math.round((ctxTokens / MAX_CONTEXT) * 100);

  return (
    <Box
      flexDirection="column"
      height={Math.max((rows ?? 24) - 1, 10)}
      borderStyle="round"
      borderColor="gray"
      borderDimColor
    >
      {/* 内容区 */}
      <ContentArea
        messages={messages}
        streamingContent={streamingContent}
        streamingReasoning={streamingReasoning}
        showReasoning={showReasoning}
        maxLines={contentLines}
      />

      {/* 思考/工具执行/完成指示器 */}
      {status === "thinking" && (
        <Box flexDirection="column" flexShrink={0}>
          <Box>
            <Text color="yellow">
              {SPINNER[spinnerFrame % SPINNER.length]}
              {executingTool
                ? ` 执行工具: ${executingTool}`
                : ` Think (${formatDuration(thinkingElapsed)} · ↓ ${formatTokens(thinkingTokens)} tokens)`}
            </Text>
          </Box>
          <Box>
            <Text dimColor>
              {"  ⎿ "}Tip: /verbose 思维链 | Ctrl+J 换行
            </Text>
          </Box>
        </Box>
      )}

      {status === "ready" && doneElapsed > 0 && (
        <Box flexShrink={0}>
          <Text dimColor>
            {"  "}Cogitated for {formatDuration(doneElapsed)}
          </Text>
        </Box>
      )}

      {/* 底部栏 */}
      <Box flexDirection="column" flexShrink={0}>
        <Text color="gray">{sep}</Text>

        {/* 输入行 */}
        <Box flexDirection="column">
          {status === "ready" &&
            (truncateInput
              ? (() => {
                  const allLines = buffer.split("\n");
                  const charCount = buffer.length;

                  return (
                    <Box>
                      <Text color="cyan" bold>
                        {"❯ "}
                      </Text>
                      <Text dimColor>
                        {"["}
                      </Text>
                      <Text>{allLines.length} 行</Text>
                      <Text dimColor>
                        {", "}
                      </Text>
                      <Text>{charCount} 字符</Text>
                      {cur === buffer.length && (
                        <>
                          <Text>{"  "}</Text>
                          <Text inverse> </Text>
                        </>
                      )}
                      <Text dimColor>{"]  输入中... (Ctrl+K 快速清空)"}</Text>
                    </Box>
                  );
                })()
              : buffer.split("\n").map((line, i, arr) => {
                  const lineStart = arr
                    .slice(0, i)
                    .reduce((s, l) => s + l.length + 1, 0);
                  const lineEnd = lineStart + line.length;
                  const isLast = i === arr.length - 1;
                  const onThis =
                    cur >= lineStart &&
                    (isLast ? cur <= lineEnd : cur < lineEnd);
                  const col = onThis ? cur - lineStart : -1;

                  return (
                    <Box key={i}>
                      <Text color="cyan" bold>
                        {i === 0 ? "❯ " : "  "}
                      </Text>
                      {onThis ? (
                        <Text>
                          {line.slice(0, col)}
                          <Text inverse> </Text>
                          {line.slice(col)}
                        </Text>
                      ) : (
                        <Text>{line}</Text>
                      )}
                    </Box>
                  );
                }))}
        </Box>

        <Text color="gray">{sep}</Text>
        <Text dimColor>
          {"  "}
          {mode === "auto" ? "⏵⏵ auto mode (tab 切换 plan)" : "📋 plan mode (tab 切换 auto)"}
        </Text>
        <Text dimColor>
          {"  ctx: "}
          {ctxPercent}% ({formatTokens(ctxTokens)}/
          {formatTokens(MAX_CONTEXT)})
        </Text>
      </Box>
    </Box>
  );
}
