#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Stash.Services;

/// <summary>
/// A mock Boltz API server for regtest testing.
/// Simulates Boltz API responses without actual Lightning/Liquid operations.
/// 
/// Usage: Enable by setting environment variable STASH_MOCK_BOLTZ=true
/// or by configuring MockBoltzOptions.Enabled = true in DI.
/// </summary>
public class MockBoltzServer : BackgroundService
{
    private readonly ILogger<MockBoltzServer> _logger;
    private readonly MockBoltzOptions _options;
    private readonly ConcurrentDictionary<string, MockSwapState> _swaps = new();
    private HttpListener? _listener;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MockBoltzServer(
        ILogger<MockBoltzServer> logger,
        MockBoltzOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public int Port => _options.Port;
    public string BaseUrl => $"http://localhost:{_options.Port}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Mock Boltz server is disabled");
            return;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_options.Port}/");

        try
        {
            _listener.Start();
            _logger.LogInformation("Mock Boltz server started on {BaseUrl}", BaseUrl);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                    _ = Task.Run(() => HandleRequest(context), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mock Boltz server error");
        }
        finally
        {
            _listener?.Stop();
            _logger.LogInformation("Mock Boltz server stopped");
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath ?? "";
            var method = request.HttpMethod;

            _logger.LogDebug("Mock Boltz: {Method} {Path}", method, path);

            object? responseBody = path switch
            {
                "/v2/pairs" => HandleGetPairs(),
                var p when p.StartsWith("/v2/swap/reverse/quote") => HandleGetReverseQuote(request),
                "/v2/swap/reverse" when method == "POST" => await HandleCreateReverseSwap(request),
                var p when p.StartsWith("/v2/swap/") => HandleGetSwapStatus(path),
                _ => null
            };

            if (responseBody == null)
            {
                response.StatusCode = 404;
                await WriteResponse(response, new { error = "Not found" });
            }
            else
            {
                response.StatusCode = 200;
                await WriteResponse(response, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling mock request");
            response.StatusCode = 500;
            await WriteResponse(response, new { error = ex.Message });
        }
        finally
        {
            response.Close();
        }
    }

    private static object HandleGetPairs()
    {
        return new
        {
            warnings = new { global = Array.Empty<string>() },
            reverse = new
            {
                // BTC -> L-BTC pair info
            }
        };
    }

    private object HandleGetReverseQuote(HttpListenerRequest request)
    {
        var query = request.QueryString;
        var invoiceAmountStr = query["invoiceAmount"];
        
        if (!long.TryParse(invoiceAmountStr, out var invoiceAmount))
        {
            return new { error = "Invalid invoiceAmount" };
        }

        // Simulate Boltz fees: ~0.5% service fee + fixed mining fee
        var serviceFee = (long)(invoiceAmount * _options.ServiceFeePercent / 100m);
        var minerFee = _options.MinerFeeSats;
        var onchainAmount = invoiceAmount - serviceFee - minerFee;

        return new BoltzReverseQuoteResponse
        {
            OnchainAmount = onchainAmount,
            ServiceFee = serviceFee,
            MinerFee = minerFee,
            ServiceFeePercent = _options.ServiceFeePercent
        };
    }

    private async Task<object> HandleCreateReverseSwap(HttpListenerRequest request)
    {
        using var reader = new System.IO.StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();
        var createRequest = JsonSerializer.Deserialize<BoltzCreateReverseSwapRequest>(body, JsonOptions);

        if (createRequest == null)
        {
            return new { error = "Invalid request body" };
        }

        // Generate a mock swap
        var swapId = $"mock-swap-{Guid.NewGuid():N}"[..24];
        var invoice = GenerateMockInvoice(createRequest.InvoiceAmount);
        
        // Calculate fees
        var serviceFee = (long)(createRequest.InvoiceAmount * _options.ServiceFeePercent / 100m);
        var minerFee = _options.MinerFeeSats;
        var onchainAmount = createRequest.InvoiceAmount - serviceFee - minerFee;

        // Store swap state for status polling
        var swapState = new MockSwapState
        {
            Id = swapId,
            Status = BoltzSwapStatus.Created,
            CreatedAt = DateTimeOffset.UtcNow,
            InvoiceAmount = createRequest.InvoiceAmount,
            OnchainAmount = onchainAmount,
            DestinationAddress = createRequest.Address
        };
        _swaps[swapId] = swapState;

        // Auto-progress the swap in the background
        _ = Task.Run(async () => await AutoProgressSwap(swapId));

        _logger.LogInformation("Mock swap created: {SwapId}, amount: {Amount} sats", 
            swapId, createRequest.InvoiceAmount);

        return new BoltzCreateReverseSwapResponse
        {
            Id = swapId,
            Invoice = invoice,
            OnchainAmount = onchainAmount,
            TimeoutBlockHeight = 999999,
            LockupAddress = $"tex1mock{swapId[..16]}",
            RefundPublicKey = "03" + new string('b', 64),
            BlindingKey = new string('c', 64)
        };
    }

    private object? HandleGetSwapStatus(string path)
    {
        // Extract swap ID from path: /v2/swap/{swapId}
        var swapId = path.Replace("/v2/swap/", "");
        
        if (!_swaps.TryGetValue(swapId, out var swapState))
        {
            return new { error = "Swap not found" };
        }

        return new BoltzSwapStatusResponse
        {
            Status = swapState.Status,
            Transaction = swapState.TransactionId != null 
                ? new BoltzTransaction { Id = swapState.TransactionId } 
                : null,
            FailureReason = swapState.FailureReason
        };
    }

    /// <summary>
    /// Automatically progresses a swap through states to simulate real Boltz behavior.
    /// </summary>
    private async Task AutoProgressSwap(string swapId)
    {
        if (!_swaps.TryGetValue(swapId, out var swap))
            return;

        try
        {
            // Wait a bit, then mark as invoice paid
            await Task.Delay(_options.SwapProgressDelayMs);
            swap.Status = BoltzSwapStatus.InvoicePaid;
            _logger.LogDebug("Mock swap {SwapId} -> InvoicePaid", swapId);

            // Wait more, then transaction in mempool
            await Task.Delay(_options.SwapProgressDelayMs);
            swap.Status = BoltzSwapStatus.TransactionMempool;
            swap.TransactionId = $"mock-tx-{Guid.NewGuid():N}"[..32];
            _logger.LogDebug("Mock swap {SwapId} -> TransactionMempool", swapId);

            // Wait more, then claimed (success)
            await Task.Delay(_options.SwapProgressDelayMs);

            if (_options.SimulateFailure)
            {
                swap.Status = BoltzSwapStatus.SwapExpired;
                swap.FailureReason = "Simulated failure for testing";
                _logger.LogDebug("Mock swap {SwapId} -> SwapExpired (simulated failure)", swapId);
            }
            else
            {
                swap.Status = BoltzSwapStatus.TransactionClaimed;
                _logger.LogInformation("Mock swap {SwapId} completed successfully", swapId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-progressing mock swap {SwapId}", swapId);
        }
    }

    private static string GenerateMockInvoice(long amountSats)
    {
        // Generate a mock BOLT11 invoice (not cryptographically valid, just for testing)
        var amountPart = amountSats switch
        {
            >= 100_000_000 => $"{amountSats / 100_000_000}",
            >= 100_000 => $"{amountSats / 1000}m",
            _ => $"{amountSats}u"
        };
        
        // GUID is 16 bytes = 32 hex chars
        var randomPart = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLower();
        return $"lntb{amountPart}1pmock{randomPart}";
    }

    private static async Task WriteResponse(HttpListenerResponse response, object body)
    {
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    /// <summary>
    /// Manually trigger a swap to fail (for testing error handling).
    /// </summary>
    public void FailSwap(string swapId, string reason)
    {
        if (_swaps.TryGetValue(swapId, out var swap))
        {
            swap.Status = BoltzSwapStatus.SwapExpired;
            swap.FailureReason = reason;
        }
    }

    /// <summary>
    /// Get current state of a mock swap (for test assertions).
    /// </summary>
    public MockSwapState? GetSwapState(string swapId)
    {
        return _swaps.TryGetValue(swapId, out var swap) ? swap : null;
    }

    /// <summary>
    /// State of a mock swap (for test assertions).
    /// </summary>
    public class MockSwapState
    {
        public string Id { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTimeOffset CreatedAt { get; set; }
        public long InvoiceAmount { get; set; }
        public long OnchainAmount { get; set; }
        public string? DestinationAddress { get; set; }
        public string? TransactionId { get; set; }
        public string? FailureReason { get; set; }
    }
}

/// <summary>
/// Configuration options for the mock Boltz server.
/// </summary>
public class MockBoltzOptions
{
    /// <summary>
    /// Whether the mock server is enabled. Default: false.
    /// Can also be enabled via STASH_MOCK_BOLTZ environment variable.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Port for the mock server. Default: 9999.
    /// </summary>
    public int Port { get; set; } = 9999;

    /// <summary>
    /// Simulated service fee percentage. Default: 0.5%.
    /// </summary>
    public decimal ServiceFeePercent { get; set; } = 0.5m;

    /// <summary>
    /// Simulated miner fee in sats. Default: 1000 sats.
    /// </summary>
    public long MinerFeeSats { get; set; } = 1000;

    /// <summary>
    /// Delay between swap state transitions in milliseconds. Default: 2000ms.
    /// </summary>
    public int SwapProgressDelayMs { get; set; } = 2000;

    /// <summary>
    /// Whether to simulate swap failures. Default: false.
    /// </summary>
    public bool SimulateFailure { get; set; }
}
