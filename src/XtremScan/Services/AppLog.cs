using Serilog;

namespace XtremScan.Services;

/// <summary>
/// Journal d'application centralisé. Utilisé par les classes statiques (sans DI).
/// Écrit dans %LocalAppData%\XtremScan\logs\.
/// Ne jamais journaliser le texte reconnu par l'OCR.
/// </summary>
public static class AppLog
{
    public static ILogger For(string source) => Log.Logger.ForContext("SourceContext", source);
}
