namespace NextDnsBetBlocker.Core.Services;

using System.Text.RegularExpressions;
using NextDnsBetBlocker.Core.Models;

public class GamblingKeywordDetector
{
    // Portuguese gambling keywords and patterns
    private static readonly HashSet<string> GamblingKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Portuguese
        "aposta", "apostas", "apostador", "apostar", "casino", "cassino", "jogo", "jogos",
        "jogo de azar", "roleta", "blackjack", "pôquer", "poker", "dados", "máquina caça-níqueis",
        "caça-níqueis", "bingo", "loteria", "lotérica", "sorteio", "rifa", "raffles",
        "tigrinho", "fortune tiger", "mines", "crash", "aviator", "bet", "betting", "wager",
        "stake", "odd", "odds", "jackpot", "bonus", "bônus", "free spin", "spin",
        "deposit", "depósito", "withdrawal", "saque", "account", "conta", "balance", "saldo",
        "betslip", "lay bet", "each-way", "parlay", "accumulator", "acumulador",
        
        // English
        "gambling", "gamble", "gambler", "game", "gaming", "luck", "lucky", "fortune",
        "prize", "prize money", "payout", "win", "winning", "loser", "loss", "luck",
        "chance", "dice", "cards", "card game", "sport betting", "sports betting",
        "live betting", "fantasy sports", "esports betting",
        
        // Suspicious payment methods
        "pix bet", "pix betting", "crypto bet", "cripto", "bitcoin", "ethereum",
        "wire transfer", "transferência", "banco", "conta corrente",
        
        // Bonus/promotional patterns
        "welcome bonus", "bônus de boas-vindas", "promotions", "promoções",
        "cashback", "free bet", "aposta grátis", "rodada grátis", "giros grátis",
        
        // Odds/betting terminology
        "decimal odds", "fractional odds", "american odds", "implied probability",
        "moneyline", "spread betting", "over/under", "handicap"
    };

    private static readonly HashSet<string> URLPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "/bet", "/betting", "/casino", "/games", "/gambling", "/play", "/wager",
        "/odds", "/aposta", "/apostas", "/jogo", "/jogos", "/caça-níqueis"
    };

    private static readonly HashSet<string> PaymentRedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "pix", "crypto", "bitcoin", "ethereum", "usdt", "usdc", "wire transfer",
        "bank transfer", "western union", "moneygram", "paypal", "skrill", "neteller"
    };

    /// <summary>
    /// Detect gambling keywords in HTML content
    /// </summary>
    public static (List<string> Keywords, int Score) DetectKeywords(string htmlContent)
    {
        var foundKeywords = new List<string>();
        var keywordScore = 0;

        if (string.IsNullOrEmpty(htmlContent))
            return (foundKeywords, 0);

        var lowercaseContent = htmlContent.ToLowerInvariant();

        // Check for keywords
        foreach (var keyword in GamblingKeywords)
        {
            if (lowercaseContent.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                foundKeywords.Add(keyword);
                keywordScore += 5; // Base score per keyword
            }
        }

        // Check for URL patterns
        foreach (var pattern in URLPatterns)
        {
            if (lowercaseContent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                foundKeywords.Add($"url-pattern:{pattern}");
                keywordScore += 10; // URL patterns are more suspicious
            }
        }

        // Check for payment methods combined with gambling keywords
        var hasPaymentMethod = PaymentRedFlags.Any(p => lowercaseContent.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (hasPaymentMethod && foundKeywords.Count > 0)
        {
            keywordScore += 15; // Significant red flag
            foundKeywords.Add("suspicious-payment-method");
        }

        // Detect forms that suggest betting (input fields for amounts, odds, selections)
        if (DetectBettingForms(lowercaseContent))
        {
            keywordScore += 20;
            foundKeywords.Add("betting-form-detected");
        }

        // Detect language patterns typical of gambling sites
        if (DetectGamblingLanguagePatterns(lowercaseContent))
        {
            keywordScore += 10;
            foundKeywords.Add("gambling-language-patterns");
        }

        return (foundKeywords, Math.Min(keywordScore, 100));
    }

    /// <summary>
    /// Detect patterns typical of betting forms
    /// </summary>
    private static bool DetectBettingForms(string content)
    {
        var patterns = new[]
        {
            @"<input.*?name.*?(amount|stake|wager|bet)",
            @"<select.*?name.*?(odds|selection)",
            @"<button.*?place bet|apostar|fazer aposta",
            @"<form.*?(bet|aposta)"
        };

        return patterns.Any(pattern => Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Detect language patterns typical of gambling sites
    /// </summary>
    private static bool DetectGamblingLanguagePatterns(string content)
    {
        var patterns = new[]
        {
            @"(?:chance|chances)\s+(?:de|to)\s+(?:ganhar|win)",
            @"(?:sua|your)\s+(?:sorte|luck)",
            @"(?:rápido|quick)\s+(?:dinheiro|money)",
            @"(?:ganhe|earn)\s+(?:agora|now)",
            @"(?:melhor|best)\s+(?:apostas|bets)"
        };

        return patterns.Any(pattern => Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Check if domain structure suggests gambling
    /// </summary>
    public static int AnalyzeDomainStructure(string domain)
    {
        var score = 0;
        var lowercaseDomain = domain.ToLowerInvariant();

        // Domain name patterns
        var suspiciousPatterns = new[]
        {
            "bet", "casino", "poker", "blackjack", "roulette", "slots", "gamble",
            "aposta", "cassino", "jogo", "bingo", "tigrinho", "crash", "mines",
            "lucky", "fortune", "win", "gold", "club", "play", "pro", "ace"
        };

        foreach (var pattern in suspiciousPatterns)
        {
            if (lowercaseDomain.Contains(pattern))
            {
                score += 15;
                break; // Only count once
            }
        }

        // Very new TLDs suspicious for gambling
        var suspiciousTlds = new[] { ".top", ".win", ".club", ".tech", ".online", ".site", ".space", ".xyz" };
        if (suspiciousTlds.Any(tld => lowercaseDomain.EndsWith(tld)))
            score += 10;

        return Math.Min(score, 100);
    }
}
