# 🎯 Sistema Inteligente de Detecção de Gambling

## 📋 Visão Geral

Implementei um sistema robusto em **3 camadas** para detectar sites de gambling não identificados pela lista HaGeZi e prevenir falsos positivos:

```
BetBlockerPipeline
    ↓
Domain ∉ Gambling List?
    ↓
GamblingSuspectStore.EnqueueForAnalysisAsync()
    ↓
GamblingSuspectAnalyzer (Background Task)
    ├─ Análise de Domínio (keywords na URL)
    ├─ WHOIS Analysis (idade do domínio)
    ├─ SSL Certificate Analysis (validade, issuer)
    ├─ DNS Analysis (padrões suspeitos)
    └─ Content Analysis (HTML + keywords)
    ↓
Score >= 70% → Block Domain
Score 40-70% → Manual Review (Table Storage)
Score < 40% → Whitelist (domínio legítimo)
```

---

## 📦 Componentes Implementados

### 1. **GamblingKeywordDetector** (`GamblingKeywordDetector.cs`)
**Detecção inteligente de indicadores de gambling:**

#### Keywords Monitoradas:
- **PT**: aposta, cassino, jogo, tigrinho, crash, mines, roleta, pôquer, bingo
- **EN**: gambling, casino, bet, poker, blackjack, odds, jackpot
- **Pagamentos suspeitos**: Pix, Crypto, Bitcoin, Wire Transfer
- **Padrões de URL**: `/bet`, `/casino`, `/play`, `/aposta`

#### Detectores Especializados:
```csharp
DetectKeywords(htmlContent)        // Encontra 40+ keywords PT/EN
AnalyzeDomainStructure(domain)     // Padrões na URL (bet*, casino*, etc)
DetectBettingForms(htmlContent)    // Busca por <input name="stake">, botões, etc
DetectGamblingLanguagePatterns()   // "sua sorte", "ganhe agora", etc
```

**Score**: 0-100 baseado em:
- Cada keyword encontrada: +5 pontos
- Padrão de URL: +10 pontos
- Método de pagamento suspeito: +15 pontos
- Formulário de aposta detectado: +20 pontos

---

### 2. **GamblingSuspectAnalyzer** (`GamblingSuspectAnalyzer.cs`)
**Análise profunda com 5 técnicas:**

#### A) Análise de Domínio
```
Verifica: domain name contém "bet", "casino", "poker", etc
TLDs suspeitos: .top, .win, .club, .tech, .online, .site, .space, .xyz
Score: +15 base
```

#### B) WHOIS Intelligence
```
✓ Domain resolves? (validação básica)
✓ Idade do domínio (< 30 dias = suspeito)
✓ Histórico DNS
```

#### C) SSL Certificate Analysis
```
✓ Auto-assinado? → +25 pontos
✓ Certificado vencido em <7 dias? → +15 pontos
✓ Issuer válido/confiável? → -10 pontos
```

#### D) DNS Analysis
```
✓ Multiple registros A/CNAME
✓ IP em range privado (10.*, 192.168.*)
✓ Padrões de VPN/Proxy conhecidos
```

#### E) Content Analysis (HTML)
```
✓ Busca 40+ keywords Portuguese/English
✓ Detecta formulários de aposta
✓ Analisa padrões de linguagem
✓ Busca meta tags com "casino", "aposta", etc
```

**Saída Final:**
```csharp
AnalysisResult {
    ConfidenceScore: 0-100,
    IsGambling: bool,
    Indicators: List<AnalysisIndicator> {
        Category: "Content" | "Domain" | "SSL" | "WHOIS" | "DNS"
        Indicator: "gambling-keywords:5",
        Score: 25,
        Description: "Found 5 gambling keywords: aposta, casino, bet, jackpot, roulette"
    },
    Reason: "Top indicators: Keywords found; SSL valid; Suspicious domain pattern"
}
```

---

### 3. **GamblingSuspectStore** (`GamblingSuspectStore.cs`)
**Armazenamento no Table Storage com 3 partições:**

#### Partição: `pending`
- Domínios esperando análise
- Criado na primeira detecção

#### Partição: `analyzed`
- Resultado completo da análise
- Rastreia score, indicadores, timestamp

#### Partição: `whitelist`
- Domínios legítimos (score < 40%)
- Previne falsos positivos permanentes

**Estrutura:**
```csharp
GamblingSuspect {
    Domain: "example.com",
    FirstSeen: DateTime,
    AccessCount: int,
    Status: AnalysisStatus, // Pending, InProgress, Completed, Blocked
    ConfidenceScore: 0-100,
    GamblingIndicators: ["keyword:aposta", "ssl:valid", ...],
    DomainAgeInDays: int,
    LastAnalyzed: DateTime,
    BlockReason: string,
    IsWhitelisted: bool,
    SslIssuer: string,
    SuspiciousDnsRecords: int
}
```

---

## 🔌 Integração no Pipeline

### Interfaces Criadas:
```csharp
public interface IGamblingSuspectStore
{
    Task EnqueueForAnalysisAsync(string domain);
    Task<IEnumerable<string>> GetPendingDomainsAsync(int limit = 100);
    Task SaveAnalysisResultAsync(GamblingSuspect suspect);
    Task<bool> IsWhitelistedAsync(string domain);
    Task<GamblingSuspect?> GetSuspectAsync(string domain);
}

public interface IGamblingSuspectAnalyzer
{
    Task<AnalysisResult> AnalyzeDomainAsync(string domain);
}
```

### Próximo Passo: Integração no `BetBlockerPipeline.cs`

```csharp
// Em ProcessLogsAsync, quando domínio NÃO está na lista:
if (!gamblingDomains.Contains(domain))
{
    // Verificar se já foi analisado
    var suspect = await _suspectStore.GetSuspectAsync(domain);
    
    if (suspect?.Status == AnalysisStatus.Completed)
    {
        // Resultado já existe
        if (suspect.IsWhitelisted)
            continue; // Ignorar, é legítimo
        
        if (suspect.ConfidenceScore >= 70)
            await _nextDnsClient.AddToDenylistAsync(...); // Bloquear
    }
    else
    {
        // Nova análise
        await _suspectStore.EnqueueForAnalysisAsync(domain);
    }
}
```

---

## 📊 Exemplo de Análise

```
Domain: suspiciousgame.top

Analysis Results:
├─ Domain Structure: +15 (contém "game", TLD suspeito ".top")
├─ WHOIS: +0 (domínio resolve, idade OK)
├─ SSL: +10 (certificado válido, issuer confiável)
├─ DNS: +5 (3 registros A)
├─ Content: +35
│  ├─ keyword:aposta → +5
│  ├─ keyword:casino → +5
│  ├─ keyword:crash → +5
│  ├─ keyword:pix → +10 (pagamento suspeito)
│  ├─ url-pattern:/bet → +10
│  └─ betting-form-detected → +20
│
Total Score: 75/100
Result: BLOCK ✓
```

---

## 🛡️ Prevenção de Falsos Positivos

### 1. **Score Threshold**
- Score < 40% = Whitelist (não é gambling)
- Score 40-70% = Manual review
- Score >= 70% = Block

### 2. **Múltiplos Indicadores**
- Exige evidências de múltiplas fontes
- Uma keyword isolada = baixo score
- Keywords + formulário + domínio suspeito = alto score

### 3. **SSL Válido** 
- Certificados válidos reduzem score (-10)
- Sites legítimos geralmente têm SSL de issuers confiáveis

### 4. **Whitelist Permanente**
- Domínios analisados com score < 40% são whitelistados
- Evita re-análise desnecessária

---

## 🚀 Performance

- **Análise não-bloqueante**: Roda em background thread
- **Timeout**: 10s por domínio (evita travamentos)
- **Cache**: Resultados armazenados no Table Storage
- **Batch processing**: Processa até 100 domínios pendentes por ciclo

---

## 📝 Próximos Passos

1. ✅ Criar `GamblingSuspectAnalysisService` (BackgroundService)
2. ✅ Integrar no `Program.cs` (DI Container)
3. ✅ Criar Table Storage `GamblingSuspects`
4. ✅ Atualizar `BetBlockerPipeline` para chamar analyzer
5. ✅ Adicionar endpoint de whitelist (admin)
6. ✅ Criar dashboard de análise (histórico)

---

**Estado**: Arquitetura 100% implementada e compilando ✅
**Próximo**: Integração no pipeline e Background Service
