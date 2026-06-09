using System.Collections.Generic;
using System.Threading.Tasks;

using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

/// <summary>进入 Plan 模式工具（AI 可主动调用来请求先规划后执行）</summary>
public class EnterPlanModeTool : ITool
{
    private readonly PlanModeService _planMode;

    public string Name => "enter_plan_mode";

    public string Description =>
        "Activates Plan Mode, where you explore the codebase and design an implementation approach before writing any code.\n" +
        "\n" +
        "## When to Use\n" +
        "Use proactively for non-trivial tasks — better to get alignment upfront than to redo work.\n" +
        "\n" +
        "**Good:**\n" +
        "- New feature implementation (e.g. \"Add a logout button\")\n" +
        "- Multiple valid approaches exist (e.g. \"Add caching to the API\")\n" +
        "- Changes affecting existing behavior or structure (e.g. \"Refactor the auth system\")\n" +
        "- Architectural decisions needed (e.g. \"Add real-time updates\")\n" +
        "- Multi-file changes (3+ files)\n" +
        "- Unclear requirements that need exploration\n" +
        "\n" +
        "**NOT for:**\n" +
        "- Typo fixes, single-line changes, simple renames\n" +
        "- Adding a single function with clear requirements\n" +
        "- Pure research/exploration (use task subagent instead)\n" +
        "\n" +
        "## What Happens\n" +
        "In plan mode you can only use read-only tools (read_file, glob, grep, task:explore, question, webfetch, read_skill) " +
        "plus write_file/edit_file ONLY on the plan file. All other write tools are blocked. " +
        "Exit with exit_plan_mode when your plan is ready.";

    public ParameterSchema Parameters => new() { Type = "object", Properties = new() };

    public EnterPlanModeTool(PlanModeService planMode)
    {
        _planMode = planMode;
    }

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        if (_planMode.IsActive)
            return Task.FromResult("Already in plan mode. Exit with exit_plan_mode when ready.");

        var planPath = _planMode.Enter();
        return Task.FromResult(
            "Entered Plan Mode. Write your plan to: `" + planPath + "`. " +
            "Use only read-only tools except for editing the plan file. Call exit_plan_mode when done.");
    }

    public ToolDefinition ToDefinition() => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };
}
