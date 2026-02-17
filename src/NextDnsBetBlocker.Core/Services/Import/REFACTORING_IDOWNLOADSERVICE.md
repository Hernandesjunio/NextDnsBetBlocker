# ✅ REFATORAÇÃO: IDownloadService para Testabilidade

## Problema Identificado

Os testes `GenericListImporterTests` falhavam porque:
- `DownloadAndParseAsync()` era privado e fazia requisições HTTP reais
- Impossível mockar o download nos testes
- Testes tentavam conectar a URLs reais durante execução

## Solução Implementada

### 1. **Criar interface `IDownloadService`**
```csharp
public interface IDownloadService
{
    Task<HashSet<string>> DownloadAndParseAsync(
        string[] sourceUrls,
        CancellationToken cancellationToken);
}
```
**Benefício**: Abstração clara para download.

### 2. **Implementar `HttpDownloadService`**
- Contém toda a lógica de HTTP
- Retry com backoff exponencial
- Parse de domínios com suporte múltiplos formatos (Tranco, Hagezi)

**Arquivo**: `src/NextDnsBetBlocker.Core/Services/Import/HttpDownloadService.cs`

### 3. **Refatorar `GenericListImporter`**
```csharp
public GenericListImporter(
    ILogger<GenericListImporter> logger,
    IListImportOrchestrator orchestrator,
    IListBlobRepository blobRepository,
    IListTableStorageRepository tableRepository,
    IDownloadService downloadService)  // ← NOVO
```

- Injetar `IDownloadService`
- Usar `_downloadService.DownloadAndParseAsync()` ao invés de privado

### 4. **Atualizar testes**
```csharp
_mockDownloadService = new Mock<IDownloadService>();
_importer = new GenericListImporter(
    _mockLogger.Object,
    _mockOrchestrator.Object,
    _mockBlobRepository.Object,
    _mockTableRepository.Object,
    _mockDownloadService.Object);  // ← MOCK AQUI

// Setup
_mockDownloadService
    .Setup(d => d.DownloadAndParseAsync(
        It.IsAny<string[]>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(testDomains);
```

**Resultado**: Testes agora **mocka o download**, não faz HTTP real!

### 5. **Registrar no DI**
```csharp
services.AddSingleton<IDownloadService, HttpDownloadService>();
```

---

## 📊 Resultado

| Métrica | Antes | Depois |
|---------|-------|--------|
| **Testes GenericListImporter** | ❌ Falhando | ✅ **5/5 Passing** |
| **HTTP Mock** | ❌ Impossível | ✅ Testável |
| **Separação de Responsabilidades** | ❌ Misto | ✅ Limpo |
| **Testabilidade** | ❌ Baixa | ✅ Alta |

---

## 🧪 Testes Atualizados

✅ `ImportAsync_WithValidDomains_CallsOrchestratorWithAddOperation`
- Mock: Download retorna 3 domínios
- Verifica: Orchestrator chamado com Add

✅ `ImportAsync_WithErrors_DoesNotSaveToBlob`
- Mock: Orchestrator retorna erro
- Verifica: Blob NÃO é salvo

✅ `ImportDiffAsync_CalculatesDiffAndCallsOrchestratorTwiceInParallel`
- Mock: Previous domains diferentes de new domains
- Verifica: Add e Remove chamados em paralelo

✅ `ImportDiffAsync_SavesNewDomainsToBlob`
- Mock: Vazio (sem metadata anterior)
- Verifica: SaveImportFile chamado

✅ `ImportAsync_CancelledTokenThrowsOperationCanceledException`
- Mock: Download throws OperationCanceledException
- Verifica: Exceção propagada

---

## 🔧 Arquitetura Após Refatoração

```
GenericListImporter (público)
├── ImportAsync()
│   └── _downloadService.DownloadAndParseAsync()  [INJETADO]
│       └── HttpDownloadService (implementação)
│           ├── HTTP download com retry
│           └── Parse domínios (Tranco + Hagezi format)
│
└── ImportDiffAsync()
    └── _downloadService.DownloadAndParseAsync()  [INJETADO]
```

**Testável**: Mock `_downloadService` sem HTTP real
**Flexível**: Implementar outros download services se necessário
**Limpo**: Separação clara de responsabilidades

---

## 📝 Commit

```
refactor: extract IDownloadService for testability and DI

- Create IDownloadService interface
- Implement HttpDownloadService with full HTTP logic
- Refactor GenericListImporter to inject IDownloadService
- Update GenericListImporterTests to mock IDownloadService
- Register HttpDownloadService in DI container
- All GenericListImporterTests passing (5/5)
```

---

## 🎯 Próximos Passos

1. ✅ Testes unitários funcionando
2. ⏭️ Rodar testes completos (11/11)
3. ⏭️ Validar em staging
4. ⏭️ Deploy em produção

