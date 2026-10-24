using KyzWheel.App.Shell;
using KyzWheel.Convert;
using KyzWheel.Core.Files;

namespace KyzWheel.App.Converter;

public sealed class ConverterController(ConversionCatalog catalog, Toasts toasts)
{
    public ConversionCatalog Catalog => catalog;

    public static string PageTitle(ConverterPage p) => p switch
    {
        ConverterPage.Images => "Imágenes",
        ConverterPage.Tools => "Herramientas",
        ConverterPage.Media => "Video y audio",
        _ => "PDF y documentos",
    };

    public static string PageColor(ConverterPage p) => p switch
    {
        ConverterPage.Images => "#FF8A3D",
        ConverterPage.Tools => "#A78BFA",
        ConverterPage.Media => "#38BDF8",
        _ => "#F87171",
    };

    public async Task<string> RunAsync(IConversion c, IReadOnlyList<string> files, CancellationToken ct = default)
    {
        if (c is IMultiConversion multi && files.Count > 1)
        {
            try
            {
                var r = await multi.RunManyAsync(files, null, ct);
                var summary = BatchSummary.Describe([r], 0);
                toasts.Result($"{c.Label} · listo", summary, [r.OutputPath]);
                return summary;
            }
            catch (ConversionException e)
            {
                toasts.Result($"{c.Label} · falló", e.Message, []);
                return e.Message;
            }
        }
        using var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2));
        var tasks = files.Select(async f =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return (Result: await c.RunAsync(f, null, ct), Error: (string?)null);
            }
            catch (ConversionException e)
            {
                return (Result: (ConversionResult?)null, Error: e.Message);
            }
            catch (Exception e)
            {
                Log.Error(e);
                return (Result: (ConversionResult?)null, Error: "Error inesperado");
            }
            finally
            {
                gate.Release();
            }
        });
        var all = await Task.WhenAll(tasks);
        var ok = all.Where(a => a.Result is not null).Select(a => a.Result!).ToList();
        var errors = all.Where(a => a.Error is not null).Select(a => a.Error!).ToList();
        var produced = ok.Where(r => r.OutputPath != r.SourcePath).Select(r => r.OutputPath).ToList();
        var body = BatchSummary.Describe(ok, errors.Count);
        if (errors.Count > 0) body += Environment.NewLine + errors[0];
        else if (produced.Count == 0 && ok.Count > 0) body += " · ya estaba optimizado";
        var title = errors.Count == 0 ? $"{c.Label} · listo" : ok.Count == 0 ? $"{c.Label} · falló" : $"{c.Label} · con errores";
        toasts.Result(title, body, produced);
        return body;
    }
}
