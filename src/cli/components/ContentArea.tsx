import React from "react";
import { Box, Text } from "ink";

interface ChatMessage {
  role: "user" | "assistant" | "reasoning" | "tool";
  content: string;
}

interface ContentLine {
  text: string;
  type:
    | "user-first"
    | "user-cont"
    | "assistant"
    | "reasoning"
    | "tool";
}

interface ContentAreaProps {
  messages: ChatMessage[];
  streamingContent: string;
  streamingReasoning: string;
  showReasoning: boolean;
  maxLines: number;
}

export default function ContentArea({
  messages,
  streamingContent,
  streamingReasoning,
  showReasoning,
  maxLines,
}: ContentAreaProps) {
  // 欢迎页：无消息、无流式内容时显示
  if (messages.length === 0 && !streamingContent && !streamingReasoning) {
    return (
      <Box flexDirection="column" flexGrow={1} overflowY="hidden">
        <Box>
          <Text>{"  "}</Text>
          <Text bold color="cyan">
            dcode
          </Text>
          <Text> — DeepSeek V4 Pro CLI Coding Assistant</Text>
        </Box>
        <Box height={1} />
        <Box>
          <Text>{"  "}</Text>
          <Text dimColor>快捷操作</Text>
        </Box>
        <Box>
          <Text>{"    "}</Text>
          <Text dimColor>Tab     </Text>
          <Text>切换 Auto / Plan 模式</Text>
        </Box>
        <Box>
          <Text>{"    "}</Text>
          <Text dimColor>Ctrl+C  </Text>
          <Text>中断生成 · 清空输入</Text>
        </Box>
        <Box>
          <Text>{"    "}</Text>
          <Text dimColor>Ctrl+J  </Text>
          <Text>多行输入换行</Text>
        </Box>
        <Box>
          <Text>{"    "}</Text>
          <Text dimColor>↑↓      </Text>
          <Text>翻阅历史消息</Text>
        </Box>
        <Box>
          <Text>{"    "}</Text>
          <Text dimColor>/verbose</Text>
          <Text> 查看思维链  </Text>
          <Text dimColor>/clear</Text>
          <Text> 清屏  </Text>
          <Text dimColor>/help</Text>
          <Text> 帮助</Text>
        </Box>
        <Box height={1} />
        <Box>
          <Text>{"  "}</Text>
          <Text dimColor>DeepSeek V4 Pro · 1M 上下文 · auto / plan mode</Text>
        </Box>
      </Box>
    );
  }

  // 展平所有内容为行
  const lines: ContentLine[] = [];

  for (const msg of messages) {
    const textLines = msg.content.split("\n");
    for (let i = 0; i < textLines.length; i++) {
      if (msg.role === "user") {
        lines.push({
          text: textLines[i],
          type: i === 0 ? "user-first" : "user-cont",
        });
      } else if (msg.role === "tool") {
        lines.push({
          text: textLines[i],
          type: "tool",
        });
      } else if (msg.role === "reasoning") {
        lines.push({ text: textLines[i], type: "reasoning" });
      } else {
        lines.push({ text: textLines[i], type: "assistant" });
      }
    }
  }

  // 流式思考链
  if (showReasoning && streamingReasoning) {
    const textLines = streamingReasoning.slice(-2000).split("\n");
    for (const line of textLines) {
      lines.push({ text: line, type: "reasoning" });
    }
  }

  // 流式内容
  if (streamingContent) {
    const textLines = streamingContent.split("\n");
    for (const line of textLines) {
      lines.push({ text: line, type: "assistant" });
    }
  }

  // 只取最后 N 行
  const visible = lines.slice(-maxLines);

  return (
    <Box flexDirection="column" flexGrow={1} overflowY="hidden">
      {visible.map((line, i) => {
        switch (line.type) {
          case "user-first":
            return (
              <Box key={`ln-${i}`}>
                <Text color="cyan" bold>
                  {"▸ "}
                </Text>
                <Text>{line.text}</Text>
              </Box>
            );
          case "user-cont":
            return (
              <Box key={`ln-${i}`}>
                <Text>{"  "}</Text>
                <Text>{line.text}</Text>
              </Box>
            );
          case "tool":
            return (
              <Box key={`ln-${i}`}>
                <Text color="blue">{"  ⚙ "}</Text>
                <Text color="blue">{line.text}</Text>
              </Box>
            );
          case "reasoning":
            return (
              <Box key={`ln-${i}`}>
                <Text dimColor>{line.text}</Text>
              </Box>
            );
          default:
            return (
              <Box key={`ln-${i}`}>
                <Text>{line.text}</Text>
              </Box>
            );
        }
      })}
    </Box>
  );
}
