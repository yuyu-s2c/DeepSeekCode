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

interface ChatMessage {
  role: "user" | "assistant" | "reasoning" | "tool";
  content: string;
}

interface AppProps {
  verbose: boolean;
  initialMessage?: string;
}

export default function App({ verbose, initialMessage }: AppProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [thinking, setThinking] = useState("");
  const [status, setStatus] = useState<"ready" | "thinking">("ready");
  const [statusText, setStatusText] = useState("");
  const { stdout } = useStdout();
  const rows = stdout?.rows ?? 24;

  const configRef = useRef(loadConfig());
  const clientRef = useRef<DeepSeekClient>();
  const registryRef = useRef<ToolRegistry>();
  const contextRef = useRef<ContextManager>();
  const showReasoningRef = useRef(verbose);
  const rlRef = useRef<readline.Interface>();

  // 初始化 Agent
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
    setStatusText(config.model);

    setApprovalCallback(async (_cmd) => false);

    // readline 输入（支持中文 IME）
    const rl = readline.createInterface({
      input: process.stdin,
      output: process.stdout,
      prompt: "",
      terminal: true,
      historySize: 200,
    });
    rlRef.current = rl;

    rl.on("line", (line) => {
      handleSubmit(line.trim());
    });

    // 启动时先渲染一次
    renderPrompt();

    if (initialMessage) {
      sendMessage(initialMessage);
    }

    return () => rl.close();
  }, []);

  // 渲染 prompt
  const renderPrompt = useCallback(() => {
    const rl = rlRef.current;
    if (!rl || status === "thinking") return;
    rl.setPrompt("\x1b[36m> \x1b[0m");
    rl.prompt();
  }, [status]);

  // 提交消息
  const handleSubmit = useCallback((text: string) => {
    if (!text) {
      renderPrompt();
      return;
    }

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
      renderPrompt();
      return;
    }

    sendMessage(text);
  }, [renderPrompt]);

  // 发送到 Agent
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
          onContentChunk: () => {},
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

      renderPrompt();
    } catch (error) {
      setMessages((prev: ChatMessage[]) => [
        ...prev,
        { role: "assistant", content: `错误: ${error instanceof Error ? error.message : String(error)}` },
      ]);
      setStatus("ready");
      setStatusText(configRef.current.model);
      renderPrompt();
    }
  }, [renderPrompt]);

  const outputHeight = (rows ?? 24) - 6;
  const visibleMessages = messages.slice(-outputHeight);

  return (
    <Box flexDirection="column" height={rows ?? 24}>
      {/* 欢迎信息 */}
      {messages.length === 0 && (
        <Box flexDirection="column" marginBottom={1}>
          <Text bold color="cyan">  dcode</Text>
          <Text dimColor>  DeepSeek V4 Pro  |  Enter 发送  |  ↑↓ 历史  |  /help</Text>
          <Text> </Text>
        </Box>
      )}

      {/* 输出区 */}
      <Box flexDirection="column" flexGrow={1} overflow="hidden">
        {visibleMessages.map((msg: ChatMessage, i: number) => (
          <Box key={i} flexDirection="row">
            <Text dimColor={msg.role === "reasoning"}>
              {msg.role === "user" && "> "}
              {msg.role === "tool" && "  ⚙ "}
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

      {/* 状态栏 */}
      <Box>
        <Text color="gray">dcode | {statusText}</Text>
        {status === "thinking" && <Text color="yellow"> ⏳</Text>}
      </Box>
    </Box>
  );
}
