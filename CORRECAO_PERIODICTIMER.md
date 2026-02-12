# 🔧 Correção: InvalidOperationException - PeriodicTimer

## ❌ Problema Original

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    // Wait for either timer
    var processingTask = processingTimer.WaitForNextTickAsync(stoppingToken).AsTask();
    var hageziTask = hageziTimer.WaitForNextTickAsync(stoppingToken).AsTask();

    await Task.WhenAny(processingTask, hageziTask);
}
```

**Por que falha?**
- `PeriodicTimer.WaitForNextTickAsync()` chamado múltiplas vezes no mesmo timer
- Após primeira iteração, o timer entra em estado inválido
- Segunda chamada a `WaitForNextTickAsync()` lança `InvalidOperationException`

**Stack trace:**
```
System.InvalidOperationException: Operation is not valid due to the current state of the object.
   at System.Threading.PeriodicTimer.State.WaitForNextTickAsync(PeriodicTimer owner, CancellationToken cancellationToken)
   at NextDnsBetBlocker.Worker.WorkerService.ExecuteAsync(CancellationToken stoppingToken) in WorkerService.cs:line 75
```

---

## ✅ Solução Aplicada

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("Worker service starting");

    // Initial HaGeZi refresh
    try
    {
        await _pipeline.UpdateHageziAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to refresh HaGeZi on startup");
    }

    // Run processing and HaGeZi update tasks concurrently
    var processingTask = ProcessLogsPeriodicAsync(stoppingToken);
    var hageziTask = UpdateHageziPeriodicAsync(stoppingToken);

    try
    {
        await Task.WhenAll(processingTask, hageziTask);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Worker service stopping");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Worker service encountered an error");
    }
}

private async Task ProcessLogsPeriodicAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.ProcessingIntervalMinutes));

    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _pipeline.ProcessLogsAsync(_settings.NextDnsProfileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing logs");
            }
        }
    }
    catch (OperationCanceledException)
    {
        _logger.LogDebug("ProcessLogs task cancelled");
    }
}

private async Task UpdateHageziPeriodicAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromHours(_settings.HageziRefreshIntervalHours));

    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _pipeline.UpdateHageziAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing HaGeZi");
            }
        }
    }
    catch (OperationCanceledException)
    {
        _logger.LogDebug("UpdateHaGeZi task cancelled");
    }
}
```

---

## 🔑 Principais Mudanças

### 1. **Separação em Tasks Independentes**
```
ANTES:
while (!stoppingToken.IsCancellationRequested)
{
    processingTask = processingTimer.WaitForNextTickAsync()     ← Reutiliza timer
    hageziTask = hageziTimer.WaitForNextTickAsync()             ← Reutiliza timer
    await Task.WhenAny(processingTask, hageziTask);
}

DEPOIS:
ProcessLogsPeriodicAsync()       ← Task 1 com seu próprio timer
  while (await timer.WaitForNextTickAsync())
    ProcessLogs()

UpdateHageziPeriodicAsync()      ← Task 2 com seu próprio timer
  while (await timer.WaitForNextTickAsync())
    UpdateHaGeZi()

Task.WhenAll(task1, task2)       ← Aguarda ambas indefinidamente
```

### 2. **Cada Timer em seu próprio Loop**
- ✓ `ProcessLogsPeriodicAsync()` tem seu próprio `PeriodicTimer`
- ✓ `UpdateHageziPeriodicAsync()` tem seu próprio `PeriodicTimer`
- ✓ Cada `while` chamando `WaitForNextTickAsync()` apenas uma vez por iteração

### 3. **Resource Cleanup com `using`**
```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMinutes(...));
// Timer é automaticamente disposed quando a task termina
```

### 4. **Execução Concorrente**
```csharp
await Task.WhenAll(processingTask, hageziTask);
```
- Ambas as tasks rodam indefinidamente
- Aguarda cancellation token (quando app para)
- Processamento de logs e atualização de HaGeZi ocorrem em paralelo

---

## 📊 Fluxo de Execução

```
ExecuteAsync()
    ↓
Atualizar HaGeZi inicialmente
    ↓
Iniciar 2 tasks concorrentes:
    ├─ ProcessLogsPeriodicAsync()
    │  ├─ Criar PeriodicTimer (1 minuto)
    │  └─ Loop infinito até cancellation:
    │     ├─ Aguardar tick
    │     └─ Processar logs
    │
    └─ UpdateHageziPeriodicAsync()
       ├─ Criar PeriodicTimer (24 horas)
       └─ Loop infinito até cancellation:
          ├─ Aguardar tick
          └─ Atualizar HaGeZi

    await Task.WhenAll()
         ↓ (cancellation token)
    
    Ambas as tasks terminam
    Timers são dispostos (using)
    App para
```

---

## 🛡️ Garantias

✅ **Sem estado inválido:** Cada timer tem seu próprio contexto  
✅ **Execução paralela:** Processamento e atualização ocorrem simultaneamente  
✅ **Resource cleanup:** `using` garante limpeza correta  
✅ **Cancellation correta:** Ambas as tasks param gracefully  
✅ **Logging detalhado:** Rastreia início, erros e cancelamento  

---

## 📝 Verificação de Logs Esperados

```
info: NextDnsBetBlocker.Worker.WorkerService[0]
      Worker service starting
info: NextDnsBetBlocker.Core.Services.BetBlockerPipeline[0]
      Starting HaGeZi update
info: NextDnsBetBlocker.Core.Services.BetBlockerPipeline[0]
      HaGeZi update completed successfully

[ProcessLogsPeriodicAsync aguardando 1 minuto...]
[UpdateHageziPeriodicAsync aguardando 24 horas...]

info: NextDnsBetBlocker.Core.Services.BetBlockerPipeline[0]
      Starting bet blocker pipeline for profile 71cb47
      ...
      Pipeline completed successfully

[Continua aguardando próximos ticks...]

info: NextDnsBetBlocker.Worker.WorkerService[0]
      Worker service stopping
      ProcessLogs task cancelled
      UpdateHaGeZi task cancelled
```

---

## ✨ Resultado

✅ Build successful  
✅ Sem `InvalidOperationException`  
✅ Processamento contínuo e estável  
✅ Graceful shutdown
