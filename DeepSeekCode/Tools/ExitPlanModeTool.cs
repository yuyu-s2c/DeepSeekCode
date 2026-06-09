using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DeepSeekCode.Models;
using DeepSeekCode.Services;

namespace DeepSeekCode.Tools;

/// <summary>退出 Plan 模式工具</summary>
public class ExitPlanModeTool : ITool
{
    private readonly PlanModeService _planMode;

    public string Name => "exit_plan_mode";

    public string Description =>
        "Exit Plan Mode and present your plan for user approval.\n" +
        "\n" +
        "## When to Use\n" +
        "Call this ONLY when your plan is complete and written to the plan file.\n" +
        "Do NOT call this prematurely or without a completed plan.\n" +
        "\n" +
        "## After Calling\n" +
        "The user will review your plan and approve or request changes.\n" +
        "If approved, you will be instructed to begin implementation.\n" +
        "If changes are requested, you may re-enter plan mode to revise.\n" +
        "\n" +
        "## Important\n" +
        "Do NOT use question tool to ask \"Is this plan okay?\" — that's exactly what this tool does.";

    public ParameterSchema Parameters => new() { Type = "object", Properties = new() };

    public ExitPlanModeTool(PlanModeService planMode)
    {
        _planMode = planMode;
    }

    public Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        if (!_planMode.IsActive)
            return Task.FromResult("Not currently in plan mode. Use enter_plan_mode to activate it.");

        var planPath = _planMode.Exit();
        var planContent = "";

        if (!string.IsNullOrEmpty(planPath) && File.Exists(planPath))
        {
            planContent = File.ReadAllText(planPath);
            if (planContent.Length > 3000)
                planContent = planContent[..3000] + "\n\n...(内容已截断)";
        }

        if (string.IsNullOrEmpty(planContent))
            planContent = "(计划文件为空或不存在)";

        return Task.FromResult(
            "已退出 Plan 模式。\n\n## 计划内容\n\n" + planContent +
            "\n\n---\n请审核以上计划。回复继续执行，或提出修改意见。");
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
