# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2024-01-15

### Added

#### Core Features
- NextDNS API client with GET /logs and POST /denylist endpoints
- Incremental log processing with checkpoint-based pagination
- HaGeZi Gambling blocklist integration and caching
- Local domain allowlist support
- Automatic domain classification and blocking
- Persistent state management via Azure Table Storage
- Blocked domains tracking for idempotency
- Rate limiting for API calls (configurable, default 5 req/s)
- Exponential backoff retry logic for 429/5xx errors

#### Infrastructure
- Console worker application for local/on-premises deployment
- Azure Functions support for serverless deployment
- Docker support with docker-compose for local development
- Azurite emulation for local Azure Storage testing
- Dependency injection throughout the application
- Structured logging with Microsoft.Extensions.Logging

#### Storage & Persistence
- Azure Table Storage: AgentState (checkpoints), BlockedDomains (registry)
- Azure Blob Storage: HaGeZi gambling domains cache
- Local file storage fallback for development
- Checkpoint system for incremental processing

#### Configuration
- Environment-based configuration (appsettings.json)
- Support for .env files
- Configurable processing intervals (logs and HaGeZi refresh)
- Customizable rate limiting

#### Documentation
- Comprehensive README with setup and deployment instructions
- Azure deployment guide with step-by-step instructions
- Technical architecture documentation
- Project structure overview
- Testing examples and templates
- Troubleshooting guide

#### Scripts & Tools
- `setup.sh` for automated initial configuration
- `test-config.sh` for NextDNS credential validation
- `.env.example` for environment variable template
- Dockerfile for containerized worker deployment

### Technical Details

#### Interfaces
- `INextDnsClient` - NextDNS API operations
- `ICheckpointStore` - Checkpoint persistence
- `IBlockedDomainStore` - Blocked domain registry
- `IHageziProvider` - HaGeZi blocklist management
- `IAllowlistProvider` - Allowlist file management
- `IBetClassifier` - Domain classification logic
- `IBetBlockerPipeline` - Pipeline orchestration

#### Pipeline Steps
1. Load and validate allowlist
2. Retrieve checkpoint (last processed timestamp)
3. Fetch NextDNS logs with pagination
4. Extract, normalize, and deduplicate domains
5. Load HaGeZi gambling list
6. Classify domains against HaGeZi
7. Block unallowlisted, unblocked, classified domains
8. Update checkpoint with latest timestamp
9. Log execution statistics

#### Statistics Tracked
- Total domains logged
- Unique domains (deduplicated)
- Domains successfully blocked
- Domains skipped (not in HaGeZi)
- Domains allowlisted
- Domains already blocked (idempotency)
- HaGeZi list size
- Pipeline execution duration

### Known Limitations

- HaGeZi list limited to gambling classification only
- Rate limiting applied only to POST /denylist calls
- Allowlist requires manual management
- No bulk domain import/export tools included
- No web UI for monitoring (use Application Insights in Azure)

### Dependencies

#### Core Packages
- .NET 10 runtime
- Azure.Data.Tables 12.8.0
- Azure.Storage.Blobs 12.19.0
- Microsoft.Extensions.* 8.0.0

#### Optional (Azure)
- Azure.Identity (for Managed Identity)
- Azure.Monitor.OpenTelemetry (for monitoring)

### Upgrade Notes

None - this is the initial release.

---

## Future Roadmap

### Planned for 2.0.0
- [ ] Web UI for monitoring and configuration
- [ ] Multiple profile support (batch processing)
- [ ] Domain import/export functionality
- [ ] Custom blocklist support (not just HaGeZi)
- [ ] Webhook notifications on blocking events
- [ ] Bulk unblock operations
- [ ] Advanced filtering rules
- [ ] Database support (SQL alternatives to Table Storage)

### Planned Improvements
- [ ] Unit tests and integration tests
- [ ] Performance optimization for large blocklists
- [ ] Caching improvements
- [ ] Configuration validation at startup
- [ ] Graceful degradation on API errors
- [ ] Batch API calls to NextDNS
- [ ] Alternative blocklist sources (Adguard, etc.)

### Potential Enhancements
- [ ] Slack/Teams notifications
- [ ] Prometheus metrics export
- [ ] Custom webhook callbacks
- [ ] Time-based blocking rules
- [ ] Geo-blocking support
- [ ] Parent-domain blocking policies
- [ ] Exception handling per domain

---

## Breaking Changes

None yet.

---

## Security Notices

### Version 1.0.0

**Important:** 
- Never commit `.env` or API keys to version control
- Use Azure Key Vault for production API key management
- Implement proper RBAC for storage account access
- Review and validate domain allowlist before deploying
- Monitor API usage to prevent unexpected costs

---

## Contributors

- Initial development and design

---

## License

MIT License - See LICENSE file for details

---

## Support & Issues

For issues, feature requests, or questions:
1. Check existing documentation (README, TECHNICAL_ARCHITECTURE, etc.)
2. Review troubleshooting section in README
3. Check Application Insights logs in Azure
4. Run test-config.sh to validate NextDNS setup
