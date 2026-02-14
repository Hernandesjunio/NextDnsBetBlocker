# 🚀 SETUP - Guia de Configuração para Novos Desenvolvedores

## ⚡ Quick Start

Siga estes passos UMA VEZ quando clonar o repositório:

---

## 1️⃣ Clone o Repositório

```bash
git clone https://github.com/seu-repo/DnsBlocker.git
cd DnsBlocker
```

---

## 2️⃣ Inicializar User Secrets

```bash
# Isso cria um secret store local (não sincroniza com Git)
cd src/NextDnsBetBlocker.Worker.Importer
dotnet user-secrets init

cd ../NextDnsBetBlocker.Worker
dotnet user-secrets init
```

---

## 3️⃣ Adicionar Seus Secrets Localmente

Veja o arquivo `secrets.template.json` para referência, então execute:

### **Para Worker.Importer**

```bash
cd src/NextDnsBetBlocker.Worker.Importer

# Adicione seus secrets (não aparece em Git!)
dotnet user-secrets set "ListImport:TrancoList:SourceUrl" "https://tranco-list.eu/download/JLKKY/full"
```

### **Para Worker (Analysis)**

```bash
cd src/NextDnsBetBlocker.Worker

# NextDNS - MUDE COM SEUS VALORES!
dotnet user-secrets set "NextDns:ApiKey" "sua-chave-api-aqui"
dotnet user-secrets set "NextDns:BaseUrl" "https://api.nextdns.io"
dotnet user-secrets set "NextDns:ProfileId" "seu-profile-id"

# HaGeZi - URLs
dotnet user-secrets set "HaGeZi:AdblockUrl" "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/adblock/gambling.txt"
dotnet user-secrets set "HaGeZi:WildcardUrl" "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists@latest/wildcard/gambling.txt"
```

---

## 4️⃣ Verificar Secrets (Opcional)

```bash
# Ver todos os secrets locais (seu machine apenas)
dotnet user-secrets list
```

---

## 5️⃣ Testar a Configuração

```bash
# Build para verificar se tudo funciona
dotnet build

# Rodar Importer (local)
dotnet run --project src/NextDnsBetBlocker.Worker.Importer

# Rodar Worker (Azure/local)
dotnet run --project src/NextDnsBetBlocker.Worker
```

---

## 📝 Arquivo de Referência: secrets.template.json

Veja na raiz do repositório o arquivo `secrets.template.json` com a estrutura de todos os secrets que você precisa adicionar.

```bash
cat secrets.template.json
```

---

## 🔐 Importante - NÃO COMMITA SEUS SECRETS!

```bash
# Seus secrets estão em:
# Windows: %APPDATA%\Microsoft\UserSecrets\<app-id>\secrets.json
# Linux/Mac: ~/.microsoft/usersecrets/<app-id>/secrets.json

# Este arquivo é IGNORADO pelo Git automaticamente
# Cada desenvolvedor tem seus próprios secrets
```

---

## ⚠️ Se Algo Não Funcionar

### Limpar User Secrets e Reiniciar

```bash
# REMOVER local secrets (cuidado!)
dotnet user-secrets clear

# Reiniciar
dotnet user-secrets init
dotnet user-secrets set "chave" "valor"
```

### Verificar appsettings.json

O arquivo `appsettings.json` tem valores DEFAULT. Se não encontrar seus secrets, usará os defaults.

```json
{
  "NextDns": {
    "ApiKey": "CHANGE_ME_IN_USER_SECRETS",
    "BaseUrl": "https://api.nextdns.io"
  }
}
```

---

## 🎯 Checklist de Setup

```
☐ Clone repositório
☐ dotnet user-secrets init (Worker.Importer)
☐ dotnet user-secrets init (Worker)
☐ Adicionar secrets locais (NextDns:ApiKey, etc)
☐ dotnet build (verificar)
☐ dotnet run (testar)
☐ ✅ Pronto para desenvolver!
```

---

## 💡 Dúvidas?

- **Como adicionar novo secret?** → `dotnet user-secrets set "chave" "valor"`
- **Como ver meus secrets?** → `dotnet user-secrets list`
- **Novo dev precisa do meu secret?** → Não! Cada um adiciona seu próprio
- **Secret está em Git?** → Não, User Secrets são locais e ignorados

---

## 🚀 Pronto!

Agora você pode desenvolver localmente com sua própria configuração, sem sincronizar secrets com Git!

**Bem-vindo ao time! 🎉**
