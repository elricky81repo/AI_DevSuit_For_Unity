public enum GptModel
{
    Gpt4oMini,
    Gpt4o,
    O4Mini,
    O3,
    Gpt5,
    Gpt5Mini,
    Gpt5Nano,
    gpt51CodexMini,
    gpt53Codex
}

public static class GptModelExtensions
{
    public static string ToApiModelString(this GptModel model)
    {
        return model switch
        {
            GptModel.Gpt4oMini => "gpt-4o-mini",
            GptModel.Gpt4o => "gpt-4o",
            GptModel.O4Mini => "o4-mini",
            GptModel.O3 => "o3",
            GptModel.Gpt5 => "gpt-5",
            GptModel.Gpt5Mini => "gpt-5-mini",
            GptModel.Gpt5Nano => "gpt-5-nano",
            GptModel.gpt51CodexMini => "gpt-5.1-codex-mini",
            GptModel.gpt53Codex => "gpt-5.3-codex",
            _ => "gpt-4o-mini"
        };
    }
}

//RECORDAR AL AGREGAR AQUÍ hay que ir a agregar en el switch de la temp en openaimanager
//
//