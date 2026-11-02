namespace Sebastian.Windows.Setup;

internal sealed class SetupException : Exception
{
    public SetupException(string message)
        : base(message)
    {
    }

    public SetupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SetupException()
    {
    }
}
