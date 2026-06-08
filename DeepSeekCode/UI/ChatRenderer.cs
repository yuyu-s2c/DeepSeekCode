using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekCode.UI;

/// <summary>
/// WebView2 聊天渲染器 — 替代 FlowDocument，用 marked.js + highlight.js 渲染 Markdown
/// </summary>
public class ChatRenderer
{
    private readonly WebView2 _webView;
    private bool _initialized;

    public ChatRenderer(WebView2 webView)
    {
        _webView = webView;
    }

    public async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.NavigateToString(HtmlTemplate);
        _initialized = true;
    }

    public async Task AppendUserMessage(string content)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(content);
        await _webView.CoreWebView2.ExecuteScriptAsync($"appendUserMessage(`{escaped}`); scrollToBottom();");
    }

    public async Task AppendSystemMessage(string content)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(content);
        await _webView.CoreWebView2.ExecuteScriptAsync($"appendSystemMessage(`{escaped}`); scrollToBottom();");
    }

    public async Task AppendAiContent(string markdown)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(markdown);
        await _webView.CoreWebView2.ExecuteScriptAsync($"appendAiContent(`{escaped}`); scrollToBottom();");
    }

    public async Task UpdateAiContent(string markdown)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(markdown);
        await _webView.CoreWebView2.ExecuteScriptAsync($"updateAiContent(`{escaped}`); scrollToBottom();");
    }

    public async Task AppendToolCard(string toolCallId, string toolName, string paramSummary)
    {
        if (!_initialized) return;
        var escapedId = EscapeJs(toolCallId);
        var escapedName = EscapeJs(toolName);
        var escapedParam = EscapeJs(paramSummary);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"appendToolCard(`{escapedId}`,`{escapedName}`,`{escapedParam}`); scrollToBottom();");
    }

    public async Task UpdateToolCardComplete(string toolCallId, bool success, string elapsed, string resultText)
    {
        if (!_initialized) return;
        var escapedId = EscapeJs(toolCallId);
        var escapedElapsed = EscapeJs(elapsed);
        var escapedResult = EscapeJs(resultText);
        var status = success ? "success" : "error";
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"updateToolCard(`{escapedId}`,`{status}`,`{escapedElapsed}`,`{escapedResult}`); scrollToBottom();");
    }

    public async Task UpdateToolCardElapsed(string toolCallId, string elapsed)
    {
        if (!_initialized) return;
        var escapedId = EscapeJs(toolCallId);
        var escapedElapsed = EscapeJs(elapsed);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            $"updateToolCardElapsed(`{escapedId}`,`{escapedElapsed}`);");
    }

    public async Task AppendThinkingCard(string thinkingBuffer)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(thinkingBuffer);
        await _webView.CoreWebView2.ExecuteScriptAsync($"appendThinkingCard(`{escaped}`); scrollToBottom();");
    }

    public async Task UpdateThinkingCard(string thinkingBuffer)
    {
        if (!_initialized) return;
        var escaped = EscapeJs(thinkingBuffer);
        await _webView.CoreWebView2.ExecuteScriptAsync($"updateThinkingCard(`{escaped}`); scrollToBottom();");
    }

    public async Task CollapseThinkingCard()
    {
        if (!_initialized) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("collapseThinkingCard();");
    }

    public async Task ClearChat()
    {
        if (!_initialized) return;
        await _webView.CoreWebView2.ExecuteScriptAsync("clearChat();");
    }

    private static string EscapeJs(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("`", "\\`")
            .Replace("$", "\\$")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
    }

    private const string HtmlTemplate = @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<script src=""https://cdn.jsdelivr.net/npm/marked@12/marked.min.js""></script>
<link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/highlight.js@11/styles/github-dark.min.css"">
<script src=""https://cdn.jsdelivr.net/npm/highlight.js@11/lib/highlight.min.js""></script>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Microsoft YaHei',sans-serif;font-size:14px;color:#1a2a38;background:#fff;padding:12px 16px 80px;line-height:1.7}
.sys-msg{color:#88a8c0;font-size:11px;padding:2px 0}
.user-msg{margin:10px 0 4px}
.user-msg .label{color:#38a0e0;font-size:11px;font-weight:600;margin-bottom:2px}
.user-msg .content{color:#1a2a38;font-size:13px}
.ai-content{color:#1a2a38;font-size:14px}
.ai-content p{margin:4px 0}
.ai-content h1,.ai-content h2,.ai-content h3,.ai-content h4{font-weight:700;margin:12px 0 4px;color:#1a2a38}
.ai-content h1{font-size:20px}.ai-content h2{font-size:17px}.ai-content h3{font-size:15px}
.ai-content code{font-family:'Cascadia Code',Consolas,monospace;font-size:13px;background:#f2f7fb;color:#c04040;padding:1px 4px;border-radius:3px}
.ai-content pre{background:#1a2a3a;border:1px solid #2a4050;border-radius:6px;padding:12px;overflow-x:auto;margin:8px 0}
.ai-content pre code{background:none;color:#a0c0d0;padding:0;font-size:13px}
.ai-content blockquote{border-left:3px solid #38a0e0;background:#f2f7fb;padding:8px 12px;margin:6px 0;color:#4a6070}
.ai-content ul,.ai-content ol{padding-left:20px;margin:4px 0}
.ai-content li{margin:2px 0}
.ai-content a{color:#38a0e0;text-decoration:underline}
.ai-content hr{border:none;border-top:1px solid #d8e6f2;margin:8px 0}
.ai-content table{border-collapse:collapse;margin:8px 0;width:100%}
.ai-content th,.ai-content td{border:1px solid #d0e0f0;padding:6px 10px;text-align:left}
.ai-content th{background:#f2f7fb;font-weight:600}

.tool-card{margin:6px 0;border-left:2px solid #38a0e0;background:#f5f8fc;border-radius:0 4px 4px 0;padding:8px 10px}
.tool-card.success{border-left-color:#58a058}
.tool-card.error{border-left-color:#c04040}
.tool-card .header{display:flex;align-items:center;gap:6px;font-size:11px}
.tool-card .header .name{font-weight:600;color:#3098d0}
.tool-card .header .param{color:#88a8c0;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.tool-card .header .time{color:#88a8c0;font-size:10px;white-space:nowrap}
.tool-card.success .header .name{color:#48a048}
.tool-card.error .header .name{color:#c04040}
.tool-card .result{margin-top:6px;font-size:11px;color:#4a6070;white-space:pre-wrap;max-height:200px;overflow-y:auto}

.think-card{margin:6px 0;background:#f5f8fc;border:1px solid #e4eef6;border-radius:4px;padding:8px 10px}
.think-card .header{font-size:11px;font-weight:600;color:#58a8d8;cursor:pointer}
.think-card .content{font-size:10px;color:#88a8c0;margin-top:4px;white-space:pre-wrap;display:none}
.think-card.expanded .content{display:block}
</style>
</head>
<body><div id=""chat""></div></body>
<script>
marked.setOptions({breaks:true,gfm:true,highlight:function(code,lang){return hljs.highlightAuto(code,lang?[lang]:[]).value}});

let aiBlock=null,thinkBlock=null;

function scrollToBottom(){window.scrollTo(0,document.body.scrollHeight)}

function appendSystemMessage(t){document.getElementById('chat').innerHTML+=`<div class=""sys-msg"">${t}</div>`}

function appendUserMessage(t){document.getElementById('chat').innerHTML+=`<div class=""user-msg""><div class=""label"">▸ 你</div><div class=""content"">${t}</div></div>`}

function appendAiContent(t){aiBlock=document.createElement('div');aiBlock.className='ai-content';aiBlock.innerHTML=marked.parse(t);document.getElementById('chat').appendChild(aiBlock)}

function updateAiContent(t){if(!aiBlock){appendAiContent(t);return}aiBlock.innerHTML=marked.parse(t)}

function appendToolCard(id,name,param){var d=document.createElement('div');d.className='tool-card';d.id='tc-'+id;d.innerHTML=`<div class=""header""><span class=""name"">⣾ ${name}</span><span class=""param"">${param||''}</span><span class=""time"">⏱ ...</span></div><div class=""result""></div>`;document.getElementById('chat').appendChild(d)}

function updateToolCard(id,status,elapsed,result){var c=document.getElementById('tc-'+id);if(!c)return;c.className='tool-card '+status;var icon=status==='success'?'✔':'✕';c.querySelector('.name').textContent=icon+' '+c.querySelector('.name').textContent.replace(/^[^ ]+ /,'');c.querySelector('.time').textContent='⏱ '+elapsed;if(result)c.querySelector('.result').textContent=result}

function updateToolCardElapsed(id,elapsed){var c=document.getElementById('tc-'+id);if(!c)return;c.querySelector('.time').textContent='⏱ '+elapsed}

function appendThinkingCard(t){thinkBlock=document.createElement('div');thinkBlock.className='think-card expanded';thinkBlock.innerHTML=`<div class=""header"" onclick=""toggleThinking(this)"">⣾ 思考中...</div><div class=""content"">${t}</div>`;document.getElementById('chat').appendChild(thinkBlock)}

function updateThinkingCard(t){if(!thinkBlock){appendThinkingCard(t);return}thinkBlock.querySelector('.content').textContent=t}

function collapseThinkingCard(){if(!thinkBlock||thinkBlock.classList.contains('collapsed'))return;thinkBlock.classList.remove('expanded');thinkBlock.classList.add('collapsed');thinkBlock.querySelector('.content').style.display='none';thinkBlock.querySelector('.header').textContent='▶ 思考过程（'+thinkBlock.querySelector('.content').textContent.length+' 字）'}

function toggleThinking(el){var p=el.parentElement;if(p.classList.contains('collapsed')){p.classList.remove('collapsed');p.classList.add('expanded');p.querySelector('.content').style.display='block'}else{p.classList.add('collapsed');p.classList.remove('expanded');p.querySelector('.content').style.display='none';p.querySelector('.header').textContent='▶ 思考过程（'+p.querySelector('.content').textContent.length+' 字）'}}

function clearChat(){document.getElementById('chat').innerHTML='';aiBlock=null;thinkBlock=null}
</script>
</html>";

}
