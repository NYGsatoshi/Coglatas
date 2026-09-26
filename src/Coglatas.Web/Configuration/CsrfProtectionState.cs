namespace Coglatas.Web.Configuration;

public sealed class CsrfProtectionState
{
    public bool IsMiddlewareActive { get; private set; }

    public void MarkMiddlewareActive()
    {
        IsMiddlewareActive = true;
    }
}
