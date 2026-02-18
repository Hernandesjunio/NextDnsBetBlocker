namespace NextDnsBetBlocker.Core;

using System;
using System.Diagnostics;

/// <summary>
/// Rastreia o progresso de processamento do ShardingProcessor
/// Calcula percentual, ETA, throughput e fornece callbacks de progresso
/// </summary>
public class ShardingProcessorProgress
{
    private readonly int _totalItems;
    private readonly Stopwatch _stopwatch;
    private int _processedItems;
    private int _lastReportedPercentage = -1;

    /// <summary>
    /// Evento dispara cuando o percentual de progresso muda
    /// </summary>
    public event Action<ProgressReportEventArgs>? ProgressChanged;

    public ShardingProcessorProgress(int totalItems)
    {
        if (totalItems <= 0)
            throw new ArgumentException("Total items deve ser maior que 0", nameof(totalItems));

        _totalItems = totalItems;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Registrar items processados e reportar progresso se percentual mudou
    /// </summary>
    public void ReportProgress(int itemsProcessed)
    {
        Interlocked.Add(ref _processedItems, itemsProcessed);

        int currentPercentage = CalculatePercentage();
        
        // Só reporta se o percentual mudou (evita spam de logs)
        if (currentPercentage != _lastReportedPercentage)
        {
            _lastReportedPercentage = currentPercentage;
            var args = CreateProgressEventArgs(currentPercentage);
            ProgressChanged?.Invoke(args);
        }
    }

    /// <summary>
    /// Obter progresso atual
    /// </summary>
    public ProgressReportEventArgs GetCurrentProgress()
    {
        return CreateProgressEventArgs(CalculatePercentage());
    }

    private int CalculatePercentage()
    {
        return (_processedItems * 100) / _totalItems;
    }

    private ProgressReportEventArgs CreateProgressEventArgs(int percentage)
    {
        _stopwatch.Stop();
        var elapsed = _stopwatch.Elapsed;
        _stopwatch.Start();

        int itemsRemaining = _totalItems - _processedItems;
        double throughput = _stopwatch.Elapsed.TotalSeconds > 0 
            ? _processedItems / _stopwatch.Elapsed.TotalSeconds 
            : 0;

        TimeSpan eta = TimeSpan.Zero;
        if (throughput > 0)
        {
            double secondsRemaining = itemsRemaining / throughput;
            eta = TimeSpan.FromSeconds(secondsRemaining);
        }

        return new ProgressReportEventArgs
        {
            Percentage = percentage,
            ProcessedItems = _processedItems,
            TotalItems = _totalItems,
            ItemsRemaining = itemsRemaining,
            Throughput = throughput,
            ETA = eta,
            Elapsed = elapsed,
            IsCompleted = percentage >= 100
        };
    }

    /// <summary>
    /// Finalizar e obter relatório final
    /// </summary>
    public ProgressReportEventArgs Finalize()
    {
        _stopwatch.Stop();
        return CreateProgressEventArgs(100);
    }
}

/// <summary>
/// Argumentos do evento de progresso
/// </summary>
public class ProgressReportEventArgs
{
    /// <summary>
    /// Percentual de progresso (0-100)
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Items já processados
    /// </summary>
    public int ProcessedItems { get; set; }

    /// <summary>
    /// Total de items a processar
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Items ainda não processados
    /// </summary>
    public int ItemsRemaining { get; set; }

    /// <summary>
    /// Throughput (operações por segundo)
    /// </summary>
    public double Throughput { get; set; }

    /// <summary>
    /// Tempo estimado para conclusão
    /// </summary>
    public TimeSpan ETA { get; set; }

    /// <summary>
    /// Tempo decorrido desde início
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Indica se processamento foi concluído
    /// </summary>
    public bool IsCompleted { get; set; }
}

/// <summary>
/// Contrato para reportador de progresso
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reportar mudança de progresso
    /// </summary>
    void Report(ProgressReportEventArgs args);
}

/// <summary>
/// Implementação de reportador com logging estruturado
/// </summary>
public class LoggingProgressReporter : IProgressReporter
{
    private readonly ILogger<LoggingProgressReporter> _logger;
    private int _lastReportedPercentage = -1;

    public LoggingProgressReporter(ILogger<LoggingProgressReporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Report(ProgressReportEventArgs args)
    {
        // Só registra se o percentual mudou
        if (args.Percentage == _lastReportedPercentage)
            return;

        _lastReportedPercentage = args.Percentage;

        string progressBar = GenerateProgressBar(args.Percentage);
        string eta = FormatTimeSpan(args.ETA);
        string elapsed = FormatTimeSpan(args.Elapsed);

        if (args.IsCompleted)
        {
            _logger.LogInformation(
                "[Progress] 100% {ProgressBar} | Completed in {Elapsed} | Throughput: {Throughput:F0} ops/s | Total items: {TotalItems:N0}",
                progressBar,
                elapsed,
                args.Throughput,
                args.TotalItems);
        }
        else
        {
            _logger.LogInformation(
                "[Progress] {Percentage,3}% {ProgressBar} | Processed: {ProcessedItems:N0}/{TotalItems:N0} | Remaining: {ItemsRemaining:N0} | ETA: {ETA} | Throughput: {Throughput:F0} ops/s",
                args.Percentage,
                progressBar,
                args.ProcessedItems,
                args.TotalItems,
                args.ItemsRemaining,
                eta,
                args.Throughput);
        }
    }

    /// <summary>
    /// Gerar barra de progresso visual
    /// </summary>
    private static string GenerateProgressBar(int percentage)
    {
        const int barWidth = 20;
        int filledWidth = (percentage * barWidth) / 100;
        string filled = new string('█', filledWidth);
        string empty = new string('░', barWidth - filledWidth);
        return $"|{filled}{empty}|";
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        else if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        else
            return $"{ts.Seconds}s";
    }
}
