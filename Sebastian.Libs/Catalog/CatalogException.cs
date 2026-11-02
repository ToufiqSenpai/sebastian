namespace Sebastian.Libs.Catalog;

public sealed class CatalogException : Exception
{
    public CatalogException()
    {
    }

    public CatalogException(string message)
        : base(message)
    {
    }

    public CatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
