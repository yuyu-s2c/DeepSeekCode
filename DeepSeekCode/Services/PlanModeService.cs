using System;
using System.IO;
using System.Text;

namespace DeepSeekCode.Services;

/// <summary>
/// Plan 模式状态管理 + 提示词生成
/// </summary>
public class PlanModeService
{
    private readonly WorkspaceService _workspaceService;
    private string? _planFilePath;

    public bool IsActive { get; private set; }

    public string? PlanFilePath => _planFilePath;

    public PlanModeService(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    /// <summary>进入 Plan 模式，返回计划文件路径</summary>
    public string Enter()
    {
        IsActive = true;
        var plansDir = Path.Combine(_workspaceService.WorkspacePath, ".deepseek-code", "plans");
        Directory.CreateDirectory(plansDir);
        var slug = GenerateSlug();
        _planFilePath = Path.Combine(plansDir, $"plan-{slug}.md");
        return _planFilePath;
    }

    /// <summary>退出 Plan 模式</summary>
    public string Exit()
    {
        IsActive = false;
        var path = _planFilePath;
        _planFilePath = null;
        return path ?? "";
    }

    /// <summary>生成注入到 system 消息的 Plan 模式提示词</summary>
    public string GenerateSystemPrompt()
    {
        var planPath = _planFilePath ?? "";
        var workspace = _workspaceService.WorkspaceName;

        return $"""
Plan mode is active. The user indicated that they do not want you to execute yet -- you MUST NOT make any edits (with the exception of the plan file mentioned below), run any non-readonly tools (including changing configs or making commits), or otherwise make any changes to the system. This supercedes any other instructions you have received.

## Plan File Info
Write your plan at: `{planPath}` using write_file or edit_file.
NOTE: This is the only file you are allowed to edit during plan mode.

## Plan Workflow

### Phase 1: Initial Understanding
Goal: Gain a comprehensive understanding of the user's request by reading code and asking questions. Critical: Use only the explore subagent type.

1. Launch up to 3 explore agents IN PARALLEL (single message, multiple tool calls) to efficiently explore the codebase.
2. Focus on understanding the request and finding existing functions, utilities, and patterns that can be reused — avoid proposing new code when suitable implementations already exist.

### Phase 2: Design
Goal: Design an implementation approach based on exploration results.

1. Think through the architecture, edge cases, error handling, and testing strategy.
2. If requirements are ambiguous, use the question tool to clarify before finalizing.
3. If multiple approaches exist, choose the best one and justify it briefly.

### Phase 3: Review
Goal: Ensure the design aligns with the user's intentions.

1. Read critical files identified during exploration to deepen understanding.
2. Use question tool to clarify any remaining questions with the user.

### Phase 4: Final Plan
Goal: Write your final plan to the plan file.

- Begin with a **Context** section: why this change is needed, what problem it solves.
- Include only the recommended approach — not all alternatives.
- List critical files to modify. For pattern-based changes, describe the pattern once.
- Reference existing functions/utilities to reuse with file paths.
- Include a **Verification** section: how to test end-to-end (run commands, tests).
- Keep the plan concise enough to scan quickly, but detailed enough to execute effectively.

### Phase 5: Call ExitPlanMode
At the end of your turn, once you are happy with your final plan file, call `exit_plan_mode` to present the plan for user approval.

This is critical: your turn should only end with either the `question` tool OR `exit_plan_mode`. Do not stop unless it's for these 2 reasons.

Important: Use `question` ONLY to clarify requirements or choose between approaches. Use `exit_plan_mode` to request plan approval. Do NOT ask about plan approval in any other way — no text questions, no question tool.

Do NOT call exit_plan_mode until your plan is complete and written to the plan file.

## Environment
- Workspace: {workspace}
- Plan file: `{planPath}`
""";
    }

    private static string GenerateSlug()
    {
        var rand = new Random();
        var adj = new[] { "swift", "bold", "calm", "deep", "eager", "fast", "keen", "lucid", "sharp", "warm" };
        var noun = new[] { "hawk", "lynx", "wolf", "bear", "deer", "fox", "owl", "pike", "swan", "wren" };
        var a = adj[rand.Next(adj.Length)];
        var n = noun[rand.Next(noun.Length)];
        return $"{a}-{n}-{rand.Next(100, 999)}";
    }
}
