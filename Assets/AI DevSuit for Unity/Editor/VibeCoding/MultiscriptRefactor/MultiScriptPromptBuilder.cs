using System.Text;

public static class MultiScriptPromptBuilder
{
    public static string Build(MultiScriptRefactorRequest request)
    {
        if (request == null)
            return string.Empty;

        return request.AnalyzeOnly
            ? BuildPlanPrompt(request)
            : BuildLegacySingleShotPrompt(request);
    }

    public static string BuildPlanPrompt(MultiScriptRefactorRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Sos un asistente experto en C# y Unity Editor tooling.");
        sb.AppendLine("Tu tarea es analizar múltiples scripts coordinadamente.");
        sb.AppendLine();
        sb.AppendLine("OBJETIVO GLOBAL:");
        sb.AppendLine(request.Goal);
        sb.AppendLine();
        sb.AppendLine("Devuelve SOLO:");
        sb.AppendLine("PLAN:");
        sb.AppendLine("- pasos concretos");
        sb.AppendLine();
        sb.AppendLine("CONTRATO GLOBAL:");
        sb.AppendLine("- firmas, dependencias, nombres de métodos y clases involucradas");
        sb.AppendLine();
        sb.AppendLine("REGLAS DE CAMBIO:");
        sb.AppendLine("- decisiones globales a aplicar en todos los archivos");
        sb.AppendLine();
        sb.AppendLine("IMPORTANTE:");
        sb.AppendLine("- No generes código.");
        sb.AppendLine("- Nombra explícitamente los archivos .cs que deben modificarse.");
        sb.AppendLine("- Si un archivo no requiere cambios, no lo incluyas.");
        sb.AppendLine("- Sé concreto.");
        sb.AppendLine();

        sb.AppendLine("ARCHIVOS INCLUIDOS:");
        sb.AppendLine();

        if (request.Scripts != null)
        {
            foreach (var script in request.Scripts)
            {
                if (script == null) continue;

                sb.AppendLine($"FILE_PATH: {script.AssetPath}");
                sb.AppendLine($"FILE_NAME: {script.FileName}");
                sb.AppendLine($"LINES: {script.LineCount}");
                sb.AppendLine($"CHARS: {script.CharacterCount}");
                sb.AppendLine("COMPACT_PREVIEW:");
                sb.AppendLine("```csharp");
                sb.AppendLine(BuildCompactPreview(script.FullContent));
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// IMPORTANTE:
    /// Aunque el parámetro compactSource siga existiendo para no romper llamadas actuales,
    /// en este flujo NO se compacta el archivo fuente.
    ///
    /// Si pedimos "archivo completo final listo para reemplazar",
    /// SIEMPRE hay que mandar el archivo completo real.
    /// </summary>
    public static string BuildFilePrompt(string globalPlanSummary, ScriptContextData script, bool compactSource)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Sos un asistente experto en C# y Unity Editor tooling.");
        sb.AppendLine();
        sb.AppendLine("PLAN GLOBAL RESUMIDO:");
        sb.AppendLine(string.IsNullOrWhiteSpace(globalPlanSummary)
            ? "(sin plan global adicional)"
            : globalPlanSummary.Trim());
        sb.AppendLine();

        sb.AppendLine("ARCHIVO OBJETIVO:");
        sb.AppendLine(script != null ? script.FileName : "(null)");
        sb.AppendLine($"RUTA: {(script != null ? script.AssetPath : "(null)")}");
        sb.AppendLine();

        sb.AppendLine("CONTENIDO ACTUAL:");
        sb.AppendLine("```csharp");
        sb.AppendLine(script != null ? script.FullContent ?? string.Empty : string.Empty);
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("TAREA:");
        sb.AppendLine("- Refactorizar SOLO este archivo.");
        sb.AppendLine("- Respetar el PLAN GLOBAL, el CONTRATO GLOBAL y las REGLAS DE CAMBIO.");
        sb.AppendLine("- Mantener compatibilidad con los otros archivos mencionados.");
        sb.AppendLine("- No eliminar funcionalidad existente salvo que el plan lo exija explícitamente.");
        sb.AppendLine("- No inventar APIs ni nombres que no estén respaldados por el código o por el plan.");
        sb.AppendLine("- Devolver el archivo completo final listo para reemplazar.");
        sb.AppendLine("- No devuelvas explicaciones.");
        sb.AppendLine("- No devuelvas texto antes ni después del bloque pedido.");
        sb.AppendLine();

        sb.AppendLine("FORMATO OBLIGATORIO:");
        sb.AppendLine($"FILE: {script.AssetPath}");
        sb.AppendLine("```csharp");
        sb.AppendLine("[código COMPLETO del archivo, listo para reemplazar]");
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static string BuildLegacySingleShotPrompt(MultiScriptRefactorRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Sos un asistente experto en C# y Unity Editor tooling.");
        sb.AppendLine("Tu tarea es analizar y refactorizar multiples scripts coordinadamente.");
        sb.AppendLine();

        sb.AppendLine("OBJETIVO GLOBAL:");
        sb.AppendLine(request.Goal);
        sb.AppendLine();

        sb.AppendLine("REGLAS IMPORTANTES:");
        sb.AppendLine("- No romper funcionalidad existente.");
        sb.AppendLine("- Mantener compatibilidad con el codigo actual.");
        sb.AppendLine("- Reutilizar logica existente si ya hay implementaciones parecidas.");
        sb.AppendLine("- Si un cambio afecta varios archivos, coordinarlos correctamente.");
        sb.AppendLine("- No inventar clases innecesarias.");
        sb.AppendLine("- Evitar crear scripts nuevos si no es necesario; solo propon uno cuando no se pueda cumplir el objetivo con los archivos proporcionados y explícitalo claramente en el plan, indicando la ruta completa.");
        sb.AppendLine("- Si un archivo no necesita cambios, indicarlo.");
        sb.AppendLine("- Responder en el formato pedido.");
        sb.AppendLine();

        sb.AppendLine("MODO:");
        sb.AppendLine("- Devolver primero un PLAN breve.");
        sb.AppendLine("- Luego devolver cada archivo modificado completo.");
        sb.AppendLine("- Si un archivo no cambia, no lo incluyas como FILE.");
        sb.AppendLine();

        sb.AppendLine("FORMATO DE RESPUESTA OBLIGATORIO:");
        sb.AppendLine("PLAN:");
        sb.AppendLine("- item 1");
        sb.AppendLine("- item 2");
        sb.AppendLine();
        sb.AppendLine("FILE: ruta/del/archivo.cs");
        sb.AppendLine("```csharp");
        sb.AppendLine("// codigo completo");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("ARCHIVOS INCLUIDOS:");
        sb.AppendLine();

        if (request.Scripts != null)
        {
            foreach (var script in request.Scripts)
            {
                if (script == null) continue;

                sb.AppendLine($"FILE_PATH: {script.AssetPath}");
                sb.AppendLine($"FILE_NAME: {script.FileName}");
                sb.AppendLine($"LINES: {script.LineCount}");
                sb.AppendLine($"CHARS: {script.CharacterCount}");
                sb.AppendLine("CONTENT:");
                sb.AppendLine("```csharp");
                sb.AppendLine(script.FullContent ?? string.Empty);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string BuildShortPlanSummary(string globalPlanSummary, int maxChars = 2200)
    {
        if (string.IsNullOrWhiteSpace(globalPlanSummary))
            return string.Empty;

        string trimmed = globalPlanSummary.Trim();

        if (trimmed.Length <= maxChars)
            return trimmed;

        return trimmed.Substring(0, maxChars) +
               "\n\n[PLAN RESUMIDO AUTOMATICAMENTE PARA REDUCIR CONTEXTO]";
    }

    private static string BuildCompactPreview(string fullContent)
    {
        if (string.IsNullOrEmpty(fullContent))
            return string.Empty;

        const int headLength = 1600;
        const int tailLength = 900;

        if (fullContent.Length <= headLength + tailLength + 200)
            return fullContent;

        string head = fullContent.Substring(0, headLength);
        string tail = fullContent.Substring(fullContent.Length - tailLength, tailLength);

        var sb = new StringBuilder();
        sb.AppendLine(head);
        sb.AppendLine();
        sb.AppendLine("// ... CONTENIDO INTERMEDIO OMITIDO SOLO PARA EL PASO DE PLAN ...");
        sb.AppendLine();
        sb.AppendLine(tail);

        return sb.ToString();
    }

    /// <summary>
    /// Se deja por compatibilidad, pero NO debe usarse para generar un archivo final completo.
    /// Solo podría servir para análisis o diagnósticos.
    /// </summary>
    private static string BuildCompactFileSource(string fullContent)
    {
        if (string.IsNullOrEmpty(fullContent))
            return string.Empty;

        const int headLength = 3500;
        const int tailLength = 2200;

        if (fullContent.Length <= headLength + tailLength + 300)
            return fullContent;

        string head = fullContent.Substring(0, headLength);
        string tail = fullContent.Substring(fullContent.Length - tailLength, tailLength);

        var sb = new StringBuilder();
        sb.AppendLine(head);
        sb.AppendLine();
        sb.AppendLine("// ... CONTENIDO INTERMEDIO OMITIDO SOLO PARA ANALISIS, NO PARA REEMPLAZO FINAL ...");
        sb.AppendLine();
        sb.AppendLine(tail);

        return sb.ToString();
    }
}