namespace First10.Infrastructure.Modules.Intake.OpenAI;

public sealed class OpenAiProviderException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public string Code { get; } = code;
}
