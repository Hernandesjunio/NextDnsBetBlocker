namespace NextDnsBetBlocker.Core.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Detecta keywords de gambling em conteúdo HTML e domínios.
/// Última linha de defesa — roda apenas em casos raros que passaram por:
///   1. Tranco Allowlist (sites confiáveis)
///   2. Hagezi Blocklist (domínios conhecidos)
///
/// Foco em PRECISÃO (evitar falsos positivos) sobre RECALL.
/// </summary>
public partial class GamblingKeywordDetector
{
    // ══════════════════════════════════════════════════════════════════
    //  ALTA CONFIANÇA — termos exclusivos de gambling (10 pts cada)
    //  Removidos: "game", "gaming", "win", "play", "luck", "cards",
    //  "balance", "deposit", "account", "banco", "saldo", "conta",
    //  "bitcoin", "pix" — genéricos demais, geram falsos positivos
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] HighConfidenceKeywords =
    [
        // Apostas (PT-BR)
        "aposta", "apostas", "apostador", "apostar",
        "cassino", "caça-níqueis", "loteria", "lotérica",
        "tigrinho", "fortune tiger",

        // Apostas (EN)
        "gambling", "gamble", "gambler",
        "betting", "wager", "sportsbook", "bookmaker", "bookie",

        // Casino games
        "blackjack", "roulette", "roleta",
        "slot machine", "baccarat", "craps",

        // Apostas esportivas
        "betslip", "parlay", "accumulator", "acumulador",
        "handicap", "moneyline", "over/under",
        "spread betting", "lay bet", "each-way",
        "live betting", "sports betting", "esports betting",

        // Jogos de azar online populares (BR)
        "aviator", "crash game", "mines game",

        // Bônus de apostas
        "free bet", "aposta grátis", "free spin",
        "rodada grátis", "giros grátis",
        "welcome bonus", "bônus de boas-vindas"
    ];

    // ══════════════════════════════════════════════════════════════════
    //  MÉDIA CONFIANÇA — podem existir em contextos legítimos (3 pts)
    // ══════════════════════════════════════════════════════════════════
    private static readonly string[] MediumConfidenceKeywords =
    [
        "casino", "poker", "pôquer", "bingo",
        "jackpot", "odds", "stake", "payout",
        "cashback", "jogo de azar", "sorteio",
        "decimal odds", "fractional odds", "american odds",
        "implied probability"
    ];

    // Regex compilada com word boundary — evita "bet" casar com "better"
    private static readonly Regex s_highConfidenceRegex = BuildWordBoundaryRegex(HighConfidenceKeywords);
    private static readonly Regex s_mediumConfidenceRegex = BuildWordBoundaryRegex(MediumConfidenceKeywords);

    // URL patterns suspeitos
    private static readonly string[] URLPatterns =
    [
        "/bet", "/betting", "/casino", "/gambling",
        "/wager", "/odds", "/aposta", "/apostas", "/sportsbook"
    ];

    // Métodos de pagamento específicos de gambling (só contam em combinação)
    private static readonly HashSet<string> PaymentRedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "skrill", "neteller" // Processadores usados quase exclusivamente por gambling
    };

    // ══════════════════════════════════════════════════════════════════
    //  Regex compilados via source generator (.NET 10)
    // ══════════════════════════════════════════════════════════════════
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"<input[^>]*name[^>]*(amount|stake|wager|bet)", RegexOptions.IgnoreCase)]
    private static partial Regex BettingInputRegex();

    [GeneratedRegex(@"<select[^>]*name[^>]*(odds|selection)", RegexOptions.IgnoreCase)]
    private static partial Regex BettingSelectRegex();

    [GeneratedRegex(@"<button[^>]*>(place bet|apostar|fazer aposta)", RegexOptions.IgnoreCase)]
    private static partial Regex BettingButtonRegex();

    [GeneratedRegex(@"<form[^>]*(bet|aposta)", RegexOptions.IgnoreCase)]
    private static partial Regex BettingFormRegex();

    [GeneratedRegex(@"(?:chance|chances)\s+(?:de|to)\s+(?:ganhar|win)", RegexOptions.IgnoreCase)]
    private static partial Regex LangPatternChanceRegex();

    [GeneratedRegex(@"(?:sua|your)\s+(?:sorte|luck)", RegexOptions.IgnoreCase)]
    private static partial Regex LangPatternLuckRegex();

    [GeneratedRegex(@"(?:rápido|quick)\s+(?:dinheiro|money)", RegexOptions.IgnoreCase)]
    private static partial Regex LangPatternMoneyRegex();

    [GeneratedRegex(@"(?:ganhe|earn)\s+(?:agora|now)", RegexOptions.IgnoreCase)]
    private static partial Regex LangPatternEarnRegex();

    [GeneratedRegex(@"(?:melhor|best)\s+(?:apostas|bets)", RegexOptions.IgnoreCase)]
    private static partial Regex LangPatternBestBetsRegex();

    /// <summary>
    /// Detecta keywords de gambling em conteúdo HTML.
    /// Remove tags HTML antes da análise para evitar falsos positivos de atributos CSS/HTML.
    /// </summary>
    public static (List<string> Keywords, int Score) DetectKeywords(string htmlContent)
    {
        var foundKeywords = new List<string>();

        if (string.IsNullOrEmpty(htmlContent))
            return (foundKeywords, 0);

        // Strip HTML tags — analisa apenas texto visível ao usuário
        var textContent = HtmlTagRegex().Replace(htmlContent, " ");
        var score = 0;

        // Alta confiança (10 pts por match)
        foreach (Match match in s_highConfidenceRegex.Matches(textContent))
        {
            var keyword = match.Value;
            if (!foundKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                foundKeywords.Add(keyword);
                score += 10;
            }
        }

        // Média confiança (3 pts por match)
        foreach (Match match in s_mediumConfidenceRegex.Matches(textContent))
        {
            var keyword = match.Value;
            if (!foundKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                foundKeywords.Add(keyword);
                score += 3;
            }
        }

        // URL patterns no conteúdo original (incluindo atributos href)
        foreach (var pattern in URLPatterns)
        {
            if (htmlContent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                foundKeywords.Add($"url-pattern:{pattern}");
                score += 10;
            }
        }

        // Métodos de pagamento red-flag (só contam se já há keywords de gambling)
        if (foundKeywords.Count > 0)
        {
            foreach (var flag in PaymentRedFlags)
            {
                if (textContent.Contains(flag, StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                    foundKeywords.Add($"payment:{flag}");
                    break;
                }
            }
        }

        // Formulários de aposta (analisa HTML original — precisa de tags)
        if (DetectBettingForms(htmlContent))
        {
            score += 20;
            foundKeywords.Add("betting-form-detected");
        }

        // Padrões linguísticos de gambling (analisa texto limpo)
        if (DetectGamblingLanguagePatterns(textContent))
        {
            score += 10;
            foundKeywords.Add("gambling-language-patterns");
        }

        return (foundKeywords, Math.Min(score, 100));
    }

    /// <summary>
    /// Detecta formulários HTML típicos de sites de aposta.
    /// Analisa o HTML original (precisa de tags).
    /// </summary>
    private static bool DetectBettingForms(string htmlContent)
    {
        return BettingInputRegex().IsMatch(htmlContent)
            || BettingSelectRegex().IsMatch(htmlContent)
            || BettingButtonRegex().IsMatch(htmlContent)
            || BettingFormRegex().IsMatch(htmlContent);
    }

    /// <summary>
    /// Detecta padrões linguísticos de persuasão comuns em sites de gambling.
    /// </summary>
    private static bool DetectGamblingLanguagePatterns(string textContent)
    {
        return LangPatternChanceRegex().IsMatch(textContent)
            || LangPatternLuckRegex().IsMatch(textContent)
            || LangPatternMoneyRegex().IsMatch(textContent)
            || LangPatternEarnRegex().IsMatch(textContent)
            || LangPatternBestBetsRegex().IsMatch(textContent);
    }

    /// <summary>
    /// Analisa a estrutura do domínio para indicadores de gambling.
    /// </summary>
    public static int AnalyzeDomainStructure(string domain)
    {
        var score = 0;
        var lowercaseDomain = domain.ToLowerInvariant();

        // Padrões de domínio de ALTA confiança para gambling
        // Removidos: "lucky", "fortune", "win", "gold", "club", "play", "pro", "ace"
        // — genéricos demais (ex: "goldengate.com", "playstation.com", "proton.me")
        string[] highConfidencePatterns =
            ["bet", "casino", "poker", "blackjack", "roulette", "slots", "gamble",
             "aposta", "cassino", "bingo", "tigrinho", "crash", "mines"];

        foreach (var pattern in highConfidencePatterns)
        {
            if (lowercaseDomain.Contains(pattern))
            {
                score += 15;
                break;
            }
        }

        // TLDs suspeitos — comuns em sites de gambling
        // Removidos: ".tech", ".space" — usados por empresas tech legítimas
        string[] suspiciousTlds = [".top", ".win", ".club", ".online", ".site", ".xyz"];
        if (suspiciousTlds.Any(tld => lowercaseDomain.EndsWith(tld)))
            score += 10;

        return Math.Min(score, 100);
    }

    /// <summary>
    /// Constrói regex compilada com word boundaries a partir de lista de keywords.
    /// Trata keywords multi-palavras e com caracteres especiais.
    /// </summary>
    private static Regex BuildWordBoundaryRegex(IEnumerable<string> keywords)
    {
        var escapedKeywords = keywords
            .Select(k => Regex.Escape(k).Replace(@"\ ", @"\s+"))
            .OrderByDescending(k => k.Length); // Padrões mais longos primeiro

        var pattern = $@"\b(?:{string.Join("|", escapedKeywords)})\b";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
