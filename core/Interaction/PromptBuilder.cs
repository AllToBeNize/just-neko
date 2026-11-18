using Microsoft.Extensions.AI;

namespace JustNeko.Core.Interaction;

/// <summary>把角色设定、角色能用的表情、近期对话和本次输入组装成发给模型的消息。</summary>
public static class PromptBuilder
{
    public static List<ChatMessage> Build(
        string rolePrompt, IReadOnlyCollection<string> emotions, IReadOnlyList<ChatMessage> history, string input)
    {
        var system = rolePrompt + "\n" + EmotionInstruction(emotions);
        return [new ChatMessage(ChatRole.System, system), .. history, new ChatMessage(ChatRole.User, input)];
    }

    private static string EmotionInstruction(IReadOnlyCollection<string> emotions) =>
        "在回复正文最开头输出一个表情标签，例如 [emotion:neutral]。可选名称包括 "
        + string.Join("、", emotions) + "。表情用名称表示，只输出一个标签，然后直接输出正文。";
}
