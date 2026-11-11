using System;

public enum AnthropicModel
{
    ClaudeHaiku45,
    ClaudeSonnet45,
    ClaudeSonnet46
}

public static class AnthropicModelMapper
{
    public static string ToApiString(AnthropicModel model)
    {
        switch (model)
        {
            case AnthropicModel.ClaudeHaiku45:
                return "claude-haiku-4-5-20251001";

            case AnthropicModel.ClaudeSonnet45:
                return "claude-sonnet-4-5-20250929";

            case AnthropicModel.ClaudeSonnet46:
                return "claude-sonnet-4-6";

            default:
                return "claude-haiku-4-5-20251001";
        }
    }

    public static AnthropicModel FromString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AnthropicModel.ClaudeHaiku45;

        if (Enum.TryParse(value, out AnthropicModel parsed))
            return parsed;

        return AnthropicModel.ClaudeHaiku45;
    }
}