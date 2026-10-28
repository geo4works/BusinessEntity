using Microsoft.JSInterop;

namespace BusinessEntity.Infrastructure.UI;

// Освобождение browser listeners допускает уже завершившееся окно, не маскируя другие JS-ошибки.
public static class BrowserLifecycle
{
    // Отменённый JS-запрос при teardown не превращает штатное закрытие в ошибку circuit.
    public static async Task UnregisterAsync(IJSRuntime js, string identifier, string elementId)
    {
        try { await js.InvokeVoidAsync(identifier, elementId); }
        catch (Exception exception) when (exception is JSDisconnectedException or OperationCanceledException)
        { System.Diagnostics.Trace.TraceInformation("Browser cleanup ended with circuit: {0}", exception.GetType().Name); }
    }
}
