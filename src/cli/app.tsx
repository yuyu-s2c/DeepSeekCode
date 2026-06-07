import React, { useState, useRef, useCallback, useEffect } from "react";
import { Box, Text, useInput, useStdout } from "ink";
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

// ---- types ----

interface ChatMessage {
  role: "user" | "assistant" | "reasoning" | "tool";
  content: string;
}

// ---- App ----

interface AppProps {
  verbose: boolean;
  initialMessage?: string;
}

export default function App({ verbose, initialMessage }: AppProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [thinking, setThinking] = useState("");
  const [inputLines, setInputLines] = useState<string[]>([""]);
  const [status, setStatus] = useState<"ready" | "thinking">("ready");
  const [statusText, setStatusText] = useState("");
  const [cursorCol, setCursorCol] = useState(0);
  const [history, setHistory] = useState<string[]>([]);
  const [historyIdx, setHistoryIdx] = useState(-1);
  const { stdout } = useStdout();
  const [rows] = useState(() => stdout?.rows ?? 24);

  const configRef = useRef(loadConfig());
  const clientRef = useRef<DeepSeekClient>();
  const registryRef = useRef<ToolRegistry>();
  const contextRef = useRef<ContextManager>();
  const showReasoningRef = useRef(verbose);

  // 初始化
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

    setApprovalCallback(async (cmd) => {
      // ink 环境下用简单的确认逻辑，默认拒绝
      return false;
    });

    setStatusText(config.model);

    if (initialMessage) {
      sendMessage(initialMessage);
    }
  }, []);

  // 发送消息
  const sendMessage = useCallback(async (text: string) => {
    const client = clientRef.current;
    const registry = registryRef.current;
    const context = contextRef.current;
    if (!client || !registry || !context) return;

    setMessages((prev: ChatMessage[]) => [...prev, { role: "user", content: text }]);
    setStatus("thinking");
    setThinking("");

    const initialMessages = context.getMessages();

    try {
      const result = await runAgentLoop(
        { userMessage: text },
        {
          client,
          softLimit: configRef.current.softLimit,
          hardLimit: configRef.current.hardLimit,
          toolRegistry: registry,
          initialMessages,
          onReasoningChunk: (chunk) => {
            setThinking((prev) => prev + chunk);
          },
          onContentChunk: () => {
            // 内容在最后统一显示
          },
          onToolCall: (name, args) => {
            const shortArgs = args.length > 50 ? args.slice(0, 47) + "..." : args;
            setMessages((prev: ChatMessage[]) => [
              ...prev,
              { role: "tool", content: `${name}(${shortArgs})` },
            ]);
          },
          onRoundExceeded: async () => false,
        }
      );

      context.addMessage({ role: "user", content: text });
      context.addMessage({
        role: "assistant",
        content: result.content,
        reasoning_content: result.reasoningContent,
        tool_calls: null,
      });

      setMessages((prev: ChatMessage[]) => [
        ...prev,
        { role: "assistant", content: result.content },
      ]);
      setThinking("");
      setStatus("ready");
      setStatusText(
        `${configRef.current.model} | ${result.totalRounds}轮 ${result.usage.promptTokens}↑ ${result.usage.completionTokens}↓`
      );
    } catch (error) {
      setMessages((prev: ChatMessage[]) => [
        ...prev,
        { role: "assistant", content: `错误: ${error instanceof Error ? error.message : String(error)}` },
      ]);
      setStatus("ready");
      setStatusText(configRef.current.model);
    }
  }, []);

  // 提交输入
  const submitInput = useCallback(() => {
    const text = inputLines.map((l: string) => l.trimEnd()).join("\n").trim();
    if (!text) return;
    // 命令
    if (text.startsWith("/")) {
      switch (text) {
        case "/help":
          setMessages((prev: ChatMessage[]) => [...prev, { role: "assistant", content: "/help /quit /clear /verbose" }]);
          break;
        case "/quit":
          process.exit(0);
        case "/clear":
          contextRef.current?.reset();
          setMessages([]);
          break;
        case "/verbose":
          showReasoningRef.current = !showReasoningRef.current;
          setMessages((prev: ChatMessage[]) => [...prev, { role: "assistant", content: `思维链: ${showReasoningRef.current ? "显示" : "隐藏"}` }]);
          break;
        default:
          setMessages((prev: ChatMessage[]) => [...prev, { role: "assistant", content: `未知: ${text}` }]);
      }
    } else {
      setHistory((prev: string[]) => [...prev, text]);
      setHistoryIdx(-1);
      sendMessage(text);
    }
    setInputLines([""]);
    setCursorCol(0);
  }, [inputLines, sendMessage]);

  // 键盘输入
  useInput((input, key) => {
    if (status === "thinking") return;

    const line = inputLines[inputLines.length - 1] ?? "";

    if (key.return) {
      if (inputLines.length === 1 && line.trim() === "") return;
      if (line.trim() === "") {
        submitInput();
      } else {
        setInputLines((prev: string[]) => [...prev, ""]);
        setCursorCol(0);
      }
      return;
    }

    if (key.delete || key.backspace) {
      if (cursorCol > 0) {
        const newLine = line.slice(0, cursorCol - 1) + line.slice(cursorCol);
        setInputLines((prev: string[]) => {
          const next = [...prev];
          next[next.length - 1] = newLine;
          return next;
        });
        setCursorCol((c: number) => c - 1);
      } else if (inputLines.length > 1) {
        setInputLines((prev: string[]) => {
          const next = prev.slice(0, -1);
          next[next.length - 1] = next[next.length - 1] + "";  // merge
          return next;
        });
      }
      return;
    }

    if (key.upArrow) {
      if (historyIdx === -1 && history.length > 0) {
        const idx = history.length - 1;
        setHistoryIdx(idx);
        setInputLines(history[idx].split("\n"));
        setCursorCol(history[idx].split("\n").pop()?.length ?? 0);
      } else if (historyIdx > 0) {
        const idx = historyIdx - 1;
        setHistoryIdx(idx);
        setInputLines(history[idx].split("\n"));
        setCursorCol(history[idx].split("\n").pop()?.length ?? 0);
      }
      return;
    }

    if (key.downArrow) {
      if (historyIdx >= 0 && historyIdx < history.length - 1) {
        const idx = historyIdx + 1;
        setHistoryIdx(idx);
        setInputLines(history[idx].split("\n"));
        setCursorCol(history[idx].split("\n").pop()?.length ?? 0);
      } else {
        setHistoryIdx(-1);
        setInputLines([""]);
        setCursorCol(0);
      }
      return;
    }

    if (key.leftArrow) {
      setCursorCol((c: number) => Math.max(0, c - 1));
      return;
    }

    if (key.rightArrow) {
      setCursorCol((c: number) => Math.min(line.length, c + 1));
      return;
    }

    if (key.escape || (key.ctrl && input === "c")) {
      setInputLines([""]);
      setCursorCol(0);
      return;
    }

    if (key.ctrl && input === "d") {
      if (line === "" && inputLines.length === 1) process.exit(0);
      return;
    }

    // 可打印字符
    if (input.length === 1 && input.charCodeAt(0) >= 32 || input === "\t") {
      const ch = input === "\t" ? "  " : input;
      setInputLines((prev: string[]) => {
        const next = [...prev];
        next[next.length - 1] = line.slice(0, cursorCol) + ch + line.slice(cursorCol);
        return next;
      });
      setCursorCol((c: number) => c + ch.length);
    }
  });

  // 计算可见消息（排除被截断的旧消息）
  const outputHeight = (rows ?? 24) - 6; // status bar + input area
  const visibleMessages = messages.slice(-outputHeight * 2); // 每条约 2 行

  return (
    <Box flexDirection="column" height={rows ?? 24}>
      {/* 输出区 */}
      <Box flexDirection="column" flexGrow={1} overflow="hidden">
        {visibleMessages.map((msg: ChatMessage, i: number) => (
          <Box key={i} flexDirection="row">
            <Text dimColor={msg.role === "reasoning"}>
              {msg.role === "user" && "> "}
              {msg.role === "tool" && "  ⚙ "}
              {msg.role === "reasoning" && "  "}
              {msg.content}
            </Text>
          </Box>
        ))}
        {thinking && showReasoningRef.current && (
          <Box>
            <Text dimColor>{thinking.slice(-200)}</Text>
          </Box>
        )}
      </Box>

      {/* 输入区 */}
      <Box flexDirection="column" borderStyle="single" borderColor="gray" paddingX={1}>
        {status === "ready" && (
          <Box flexDirection="column">
            {inputLines.map((line: string, i: number) => (
              <Box key={i}>
                <Text color="cyan">
                  {i === 0 ? "> " : "| "}
                </Text>
                <Text>{line}</Text>
              </Box>
            ))}
          </Box>
        )}
        {status === "thinking" && (
          <Text dimColor>⏳ 思考中...</Text>
        )}
      </Box>

      {/* 状态栏 */}
      <Box>
        <Text color="gray">dcode</Text>
        <Text> | </Text>
        <Text>{statusText}</Text>
        {status === "thinking" && <Text color="yellow"> ⏳</Text>}
      </Box>
    </Box>
  );
}

