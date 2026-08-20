using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using Strata.Core.Errors;
using Strata.Core.Model;
using Strata.Sbom;
using Strata.Web.Services;

namespace Strata.Web.Components.Pages;

/// <summary>
/// Public web demo (User Story 6, contracts/web-demo.md). Sample-first, with an optional capped upload;
/// runs the same <see cref="Strata.Core.IScanner"/> as the CLI (Principle I) so identical bytes produce
/// identical results in both places.
/// </summary>
public sealed partial class Demo : ComponentBase
{
    private const int RevealDelayMilliseconds = 220;

    [Inject]
    private ScanDemoService DemoService { get; set; } = default!;

    [Inject]
    private PerIpScanThrottle Throttle { get; set; } = default!;

    [Inject]
    private ClientCircuitContext ClientContext { get; set; } = default!;

    [Inject]
    private IOptions<DemoOptions> Options { get; set; } = default!;

    private DemoOptions Config => Options.Value;

    private bool _isBusy;
    private string? _sourceName;
    private string? _errorMessage;
    private ScanProgress? _progress;
    private ScanResult? _result;
    private int _revealedCount;
    private bool _detailsRevealed;
    private readonly HashSet<int> _expandedComponents = [];
    private bool _showSbom;
    private string? _sbomText;
    private string? _sbomError;

    private string MaxUploadDisplay => FormatBytes(Config.MaxUploadBytes);

    private async Task SelectSampleAsync(string sampleId)
    {
        if (!BeginScan())
        {
            return;
        }

        try
        {
            (Stream binary, string name) = await DemoService.OpenSampleAsync(sampleId, CancellationToken.None);
            await using (binary)
            {
                await RunScanAsync(binary, name);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _errorMessage = $"Could not load sample: {ex.Message}";
            _isBusy = false;
        }
    }

    private async Task OnFileSelectedAsync(InputFileChangeEventArgs e)
    {
        IBrowserFile file = e.File;

        // FR-025 / US6 sc.3: over-cap uploads are rejected on metadata alone — nothing is read and no
        // scan is attempted.
        if (file.Size > Config.MaxUploadBytes)
        {
            _errorMessage =
                $"'{file.Name}' is {FormatBytes(file.Size)}, over the {MaxUploadDisplay} demo upload limit — " +
                "rejected without scanning.";
            _result = null;
            return;
        }

        if (!BeginScan())
        {
            return;
        }

        try
        {
            // Retention = none (FR-025): read fully into an in-memory buffer, bounded by the same cap as
            // defence in depth. Nothing ever touches disk, and the buffer is discarded once this method
            // returns.
            await using Stream browserStream = file.OpenReadStream(Config.MaxUploadBytes);
            using var buffer = new MemoryStream();
            await browserStream.CopyToAsync(buffer);
            buffer.Position = 0;

            await RunScanAsync(buffer, file.Name);
        }
        catch (IOException ex)
        {
            _errorMessage = $"Upload rejected: {ex.Message}";
            _isBusy = false;
        }
    }

    private bool BeginScan()
    {
        if (_isBusy)
        {
            return false;
        }

        // FR-025 / US6 sc.4: abuse control without login — throttle scan attempts per client.
        if (!Throttle.TryAcquire(ClientContext.RemoteIp))
        {
            _errorMessage = "You're scanning faster than the demo allows — please wait a moment and try again.";
            return false;
        }

        _isBusy = true;
        _errorMessage = null;
        _result = null;
        _progress = null;
        _revealedCount = 0;
        _detailsRevealed = false;
        _showSbom = false;
        _sbomText = null;
        _sbomError = null;
        _expandedComponents.Clear();
        return true;
    }

    private async Task RunScanAsync(Stream binary, string name)
    {
        _sourceName = name;
        StateHasChanged();

        // Progress<T> captures the Blazor renderer's SynchronizationContext at construction time (we are
        // inside a component event handler here), so Report() below — called from the background thread
        // running Scan() — automatically marshals back onto it before StateHasChanged() runs.
        var progress = new Progress<ScanProgress>(p =>
        {
            _progress = p;
            StateHasChanged();
        });

        try
        {
            ScanResult result = await Task.Run(
                () => DemoService.Scan(binary, name, Config.MaxUploadBytes, progress));

            _result = result;
            StateHasChanged();
            await RevealComponentsAsync(result);
        }
        catch (UnsupportedFormatException ex)
        {
            _errorMessage = ex.Message;
        }
        catch (OutOfEnvelopeException ex)
        {
            _errorMessage = ex.Message;
        }
        catch (CorpusSchemaMismatchException ex)
        {
            _errorMessage = $"Demo corpus unavailable: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            StateHasChanged();
        }
    }

    private async Task RevealComponentsAsync(ScanResult result)
    {
        // IScanner streams stage progress (IProgress<ScanProgress>), not partial components — the engine
        // returns the whole ScanResult atomically. To deliver the progressive "libraries light up" UX
        // (FR-024) within that contract, components are unveiled one at a time here, in the engine's own
        // deterministic order (confidence descending, then name) — the "confirmation order" invariant.
        foreach (IdentifiedComponent _ in result.Components)
        {
            await Task.Delay(RevealDelayMilliseconds);
            _revealedCount++;
            StateHasChanged();
        }

        _detailsRevealed = true;
        StateHasChanged();
    }

    private void ToggleEvidence(int index)
    {
        if (!_expandedComponents.Add(index))
        {
            _expandedComponents.Remove(index);
        }
    }

    private void ToggleSbom()
    {
        _showSbom = !_showSbom;
        if (_showSbom && _sbomText is null && _sbomError is null && _result is not null)
        {
            try
            {
                _sbomText = SbomWriter.Emit(
                    _result, SbomFormat.CycloneDx, new SbomOutputOptions { Deterministic = true });
            }
            catch (InvalidOperationException ex)
            {
                _sbomError = ex.Message;
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double Kb = 1024;
        const double Mb = Kb * 1024;
        return bytes switch
        {
            >= (long)Mb => $"{bytes / Mb:0.#} MB",
            >= (long)Kb => $"{bytes / Kb:0.#} KB",
            _ => $"{bytes} B",
        };
    }

    private static string ConfidenceClass(double confidence) => confidence switch
    {
        >= 0.8 => "confidence-high",
        >= 0.5 => "confidence-medium",
        _ => "confidence-low",
    };

    private static string ConfidenceLabel(double confidence) => confidence switch
    {
        >= 0.8 => "High confidence",
        >= 0.5 => "Medium confidence",
        _ => "Low confidence",
    };
}
