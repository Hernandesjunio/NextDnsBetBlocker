# Documentation Index

Complete guide to all documentation files in the NextDNS Bet Blocker project.

## 📋 Start Here

**New to the project?** Start with these in order:

1. **[EXECUTIVE_SUMMARY.md](EXECUTIVE_SUMMARY.md)** (5 min read)
   - What is NextDNS Bet Blocker?
   - Key features and benefits
   - Cost analysis
   - Quick start overview

2. **[README.md](README.md)** (15 min read)
   - Setup instructions
   - Local Docker deployment
   - Configuration guide
   - Troubleshooting

3. **[AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md)** (if deploying to cloud)
   - Step-by-step Azure setup
   - Resource creation
   - Configuration
   - Monitoring

## 🏗️ Architecture & Design

Understand how the system works:

- **[TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)**
  - System design diagram
  - Data flow diagrams
  - API integration details
  - Storage schema
  - Error handling strategy
  - Performance characteristics
  - Security considerations

- **[PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)**
  - File organization
  - Project layout
  - Component descriptions
  - Dependency tree
  - Configuration hierarchy

## 🛠️ Development & Operations

- **[COMMANDS_REFERENCE.md](COMMANDS_REFERENCE.md)**
  - Docker commands
  - Azure CLI commands
  - Testing commands
  - Troubleshooting commands
  - Useful one-liners
  - Emergency procedures

- **[TESTING_EXAMPLES.md](TESTING_EXAMPLES.md)**
  - Unit test templates
  - Integration test patterns
  - Mock examples
  - Test structure recommendations

- **[CHANGELOG.md](CHANGELOG.md)**
  - Version history
  - What's new in current version
  - Breaking changes
  - Roadmap for future versions

## 📁 Key Configuration Files

- **[.env.example](.env.example)**
  - Environment variable template
  - Copy to `.env` for local setup

- **[docker-compose.yml](docker-compose.yml)**
  - Local development orchestration
  - Services: Azurite, Worker
  - Volume configuration

- **[src/NextDnsBetBlocker.Worker/appsettings.json](src/NextDnsBetBlocker.Worker/appsettings.json)**
  - Worker default configuration
  - Logging levels

- **[src/NextDnsBetBlocker.Worker/appsettings.Development.json](src/NextDnsBetBlocker.Worker/appsettings.Development.json)**
  - Development-specific settings
  - Azurite endpoints

- **[src/NextDnsBetBlocker.Worker/allowlist.txt](src/NextDnsBetBlocker.Worker/allowlist.txt)**
  - Local domain allowlist
  - Domains to never block

## 🚀 Quick Links by Task

### I want to...

**Get Started Immediately**
→ [README.md - Getting Started](README.md#getting-started)
→ [Quick Docker Setup](#quick-docker-setup-5-minutes)

**Deploy to Production**
→ [AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md)
→ [README.md - Azure Deployment](README.md#deploying-to-azure)

**Understand the Architecture**
→ [TECHNICAL_ARCHITECTURE.md](TECHNICAL_ARCHITECTURE.md)
→ [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)

**Configure Settings**
→ [README.md - Configuration Options](README.md#configuration-options)
→ [.env.example](.env.example)

**Debug an Issue**
→ [COMMANDS_REFERENCE.md - Troubleshooting](COMMANDS_REFERENCE.md#troubleshooting-commands)
→ [README.md - Troubleshooting](README.md#troubleshooting)
→ Run: `./test-config.sh`

**Write Tests**
→ [TESTING_EXAMPLES.md](TESTING_EXAMPLES.md)

**Manage Azure Resources**
→ [COMMANDS_REFERENCE.md - Azure](COMMANDS_REFERENCE.md#azure-deployment)
→ [AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md)

**View System Metrics**
→ [TECHNICAL_ARCHITECTURE.md - Monitoring](TECHNICAL_ARCHITECTURE.md#monitoring--alerting)
→ [README.md - Statistics](README.md#statistics--logging)

## 📊 Documentation Structure

```
NextDnsBetBlocker/
├── EXECUTIVE_SUMMARY.md      ← Overview & benefits
├── README.md                 ← Main guide (setup, config, troubleshooting)
├── AZURE_DEPLOYMENT.md       ← Cloud deployment guide
├── TECHNICAL_ARCHITECTURE.md ← Deep technical details
├── PROJECT_STRUCTURE.md      ← Code organization
├── COMMANDS_REFERENCE.md     ← CLI commands & scripts
├── TESTING_EXAMPLES.md       ← Test templates
├── CHANGELOG.md              ← Version history
├── PROJECT_DOCUMENTATION.md  ← This file
│
├── Configuration Files
├── .env.example             ← Environment template
├── docker-compose.yml       ← Docker setup
├── appsettings.json files   ← App configuration
├── Dockerfile               ← Container build
│
├── Helper Scripts
├── setup.sh                 ← Interactive setup
├── test-config.sh           ← Configuration validation
│
└── Source Code
    ├── src/NextDnsBetBlocker.Core/
    ├── src/NextDnsBetBlocker.Worker/
    └── src/NextDnsBetBlocker.FunctionApp/
```

## 🔍 Find Information By Topic

### Setup & Installation
- [README.md - Getting Started](README.md#getting-started)
- [setup.sh](setup.sh) - Automated setup script
- [AZURE_DEPLOYMENT.md - Prerequisites](AZURE_DEPLOYMENT.md#prerequisites)

### Configuration
- [README.md - Configuration Options](README.md#configuration-options)
- [.env.example](.env.example)
- [TECHNICAL_ARCHITECTURE.md - Configuration Hierarchy](#configuration-hierarchy)

### Deployment
- **Local:** [README.md - Running Locally](README.md#running-locally-with-docker)
- **Azure:** [AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md)
- **Docker:** [docker-compose.yml](docker-compose.yml)

### API Integration
- [TECHNICAL_ARCHITECTURE.md - API Integration](TECHNICAL_ARCHITECTURE.md#api-integration)
- [README.md - API Configuration](README.md#api-configuration)

### Storage & Data
- [TECHNICAL_ARCHITECTURE.md - Storage Schema](TECHNICAL_ARCHITECTURE.md#storage-schema)
- [PROJECT_STRUCTURE.md - Data Flow](PROJECT_STRUCTURE.md#data-flow)

### Monitoring & Logging
- [README.md - Statistics & Logging](README.md#statistics--logging)
- [TECHNICAL_ARCHITECTURE.md - Monitoring](TECHNICAL_ARCHITECTURE.md#monitoring--alerting)
- [COMMANDS_REFERENCE.md - Log Analysis](COMMANDS_REFERENCE.md#log-analysis)

### Troubleshooting
- [README.md - Troubleshooting](README.md#troubleshooting)
- [COMMANDS_REFERENCE.md - Troubleshooting](COMMANDS_REFERENCE.md#troubleshooting-commands)
- [COMMANDS_REFERENCE.md - Emergency Procedures](COMMANDS_REFERENCE.md#emergency-procedures)

### Performance & Optimization
- [TECHNICAL_ARCHITECTURE.md - Performance](TECHNICAL_ARCHITECTURE.md#performance-characteristics)
- [COMMANDS_REFERENCE.md - Performance Profiling](COMMANDS_REFERENCE.md#performance-profiling)

### Security
- [README.md - Security Considerations](README.md#security-considerations)
- [TECHNICAL_ARCHITECTURE.md - Security](TECHNICAL_ARCHITECTURE.md#security-considerations)

### Testing
- [TESTING_EXAMPLES.md](TESTING_EXAMPLES.md)
- [COMMANDS_REFERENCE.md - Testing](COMMANDS_REFERENCE.md#testing-configuration)

## 📚 Related Resources

### External Documentation
- **NextDNS API**: https://api.nextdns.io/
- **HaGeZi Project**: https://github.com/hagezi/dns-blocklists
- **.NET 10 Docs**: https://learn.microsoft.com/dotnet/
- **Azure Functions**: https://docs.microsoft.com/azure/azure-functions/
- **Docker Docs**: https://docs.docker.com/

### Community Resources
- **Azure CLI Reference**: https://docs.microsoft.com/cli/azure/
- **Azure Table Storage**: https://learn.microsoft.com/azure/storage/tables/
- **Application Insights**: https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview

## ❓ FAQ by Documentation

### README.md
- How do I get started?
- What are the prerequisites?
- How do I configure the application?
- How do I run it locally?
- How do I deploy to Azure?
- What do the statistics mean?
- How do I troubleshoot issues?

### TECHNICAL_ARCHITECTURE.md
- How does the system work internally?
- What's the data flow?
- How does API communication work?
- What's the storage schema?
- How are errors handled?
- What are the security considerations?
- What are the performance characteristics?

### PROJECT_STRUCTURE.md
- Where is each file?
- What does each file do?
- How are dependencies organized?
- What's the configuration hierarchy?
- How does data flow through the system?

### AZURE_DEPLOYMENT.md
- How do I deploy to Azure?
- What Azure resources do I need?
- How do I monitor the deployment?
- What are the costs?
- How do I troubleshoot Azure issues?

### COMMANDS_REFERENCE.md
- What Docker commands are available?
- What Azure CLI commands can I use?
- How do I test the configuration?
- How do I monitor the system?
- What do I do in an emergency?

## 🎯 Recommended Reading Order

### For First-Time Users
1. EXECUTIVE_SUMMARY.md (5 min)
2. README.md - Getting Started (10 min)
3. Run setup.sh (2 min)
4. docker-compose up (1 min)

### For Developers
1. PROJECT_STRUCTURE.md (10 min)
2. TECHNICAL_ARCHITECTURE.md (20 min)
3. TESTING_EXAMPLES.md (10 min)
4. Read source code in src/

### For Operations
1. README.md (15 min)
2. AZURE_DEPLOYMENT.md (if deploying to cloud)
3. COMMANDS_REFERENCE.md (reference as needed)
4. Set up monitoring

### For Architects
1. EXECUTIVE_SUMMARY.md (5 min)
2. TECHNICAL_ARCHITECTURE.md (30 min)
3. PROJECT_STRUCTURE.md (15 min)
4. Review code in src/NextDnsBetBlocker.Core/

## 📞 Getting Help

1. **Check the docs**: Search above for your topic
2. **Run test-config.sh**: Validates your NextDNS setup
3. **Check logs**: `docker-compose logs worker`
4. **Review TECHNICAL_ARCHITECTURE.md**: Understand how it works
5. **Consult COMMANDS_REFERENCE.md**: Find diagnostic commands

## 🔄 Keeping Documentation Updated

When changes are made:
- Update CHANGELOG.md with version and changes
- Update relevant guide (README, TECHNICAL_ARCHITECTURE, etc.)
- Update PROJECT_STRUCTURE.md if file structure changes
- Update this index if major sections change

## License

All documentation is MIT Licensed - free to use and modify.
