# Comprehensive SDLC & Operational Lifecycle for FraudPlatform

This document defines the rigorous, end-to-end lifecycle for the `FraudPlatform` project. It covers everything from writing the first line of code to heavy post-production testing and maintenance, ensuring high availability, fault tolerance, and security for a critical financial application.

---

## 1. Roles and Cross-Team Responsibilities
To safely deploy and maintain this platform at an enterprise level, various parties must collaborate:
- **Developers (Dev)**: Write business logic, AI integrations, unit tests, and handle local testing.
- **Quality Assurance (QA / SDET)**: Write automated End-to-End (E2E) tests, execute load/performance tests, and perform manual User Acceptance Testing (UAT).
- **DevOps / Site Reliability Engineers (SRE)**: Manage CI/CD pipelines, infrastructure as code (Terraform/Bicep for RabbitMQ/SQL Server), and production monitoring.
- **Security Operations (SecOps)**: Run penetration tests, code security scans, and monitor AI prompt-injection vulnerabilities.

---

## 2. Coding Standards & Pre-Commit Phase
Before code even leaves a developer's machine, strict standards apply to ensure long-term maintainability.
- **Style Guidelines**: Enforce .NET coding conventions across the team using an `.editorconfig` file and **StyleCop Analyzers**.
- **Test-Driven Approach**: Developers must write xUnit/NUnit tests for business logic in `Fraud.Application` and mock the AI `IRiskEvaluationService`.
- **Pre-Commit Hooks**: Use tools like `Husky.Net` to automatically run `dotnet format` and execute unit tests locally before allowing a developer to `git commit`.

---

## 3. Continuous Integration (CI) - The Build Phase
When a developer finishes a feature and opens a Pull Request (PR) against the `staging` branch, the CI pipeline (e.g., GitHub Actions, Azure DevOps) triggers automatically.

1. **Build & Restore**: `dotnet build` runs to ensure the code compiles without errors.
2. **Static Code Analysis**: Tools like **SonarQube** scan the code for bugs, code smells, and security vulnerabilities (SAST). The PR is automatically blocked if overall code coverage drops below 80%.
3. **Automated Testing**: 
   - Fast unit tests are executed.
   - **Testcontainers** (Docker-in-Docker) spins up temporary, isolated RabbitMQ and SQL Server instances to run **Integration Tests** (e.g., ensuring the Worker can successfully read from a real queue and write to a real DB).
4. **Peer Review**: At least one senior developer must review and approve the PR, specifically checking the AI logic and architecture.

---

## 4. Staging & Heavy Testing (Pre-Production)
Once merged to `staging`, the CD pipeline deploys the application to a Staging environment that perfectly mirrors Production infrastructure. Here, heavy testing occurs.

1. **Functional Testing**: QA runs automated E2E tests to simulate GraphQL API calls and verify the Worker processes them, scores the risk, and generates alerts in the DB.
2. **Load & Performance Testing**: Because financial platforms must handle high throughput, QA uses tools like **k6** or **JMeter** to blast the GraphQL API with 10,000+ requests/second. This ensures MassTransit and RabbitMQ scale appropriately without dropping messages and that the AI API rate limits aren't exceeded.
3. **Security Testing (DAST)**: SecOps runs automated dynamic application security testing against the Staging GraphQL endpoint to check for vulnerabilities.
4. **Chaos Engineering (Fault Tolerance Test)**: SREs intentionally "break" the staging environment. They might kill a RabbitMQ node or block the network to the AI API to verify that MassTransit's Retry Policies trigger correctly and that messages safely fall back to the Dead Letter Queue (DLQ).

---

## 5. Production Deployment Strategy
To guarantee zero downtime for payment processing, the project utilizes a **Blue-Green Deployment** strategy.

1. **Sign-off**: The Product Owner signs off on the Staging environment.
2. **Database Migrations**: Entity Framework Core migrations are applied to the production database carefully. Migrations must always be backward compatible.
3. **Blue-Green Switch**: 
   - The new version of the Api and Worker (Green) is deployed alongside the old version (Blue).
   - Traffic is gradually routed to Green.
   - If error rates spike, the load balancer instantly reverts traffic back to Blue, providing an immediate safety net.

---

## 6. Post-Production Maintenance & Monitoring
Once live, the focus shifts to observability, incident response, and continuous tuning.

### Observability & Telemetry Stack
- **Structured Logging**: `Fraud.Api` and `Fraud.Worker` use Serilog to write structured JSON logs (including the `TransactionId` in every log line for easy tracing).
- **Centralized Dashboard**: Logs are ingested into an **ELK Stack** (Elasticsearch, Logstash, Kibana) or **Datadog**.
- **Metrics & Tracing**: OpenTelemetry is used to track the exact time a request takes from the GraphQL API, through the RabbitMQ queue, to the AI Risk evaluation. Prometheus scrapes these metrics for Grafana dashboards.

### Post-Prod Maintenance Activities
1. **DLQ Triage (Daily Operations)**: SREs monitor the `transaction-queue_error` (Dead Letter Queue). If a message ends up here (e.g., because the AI provider was down for 10 minutes), they investigate the error payload and manually replay the message back into the main queue once the issue is resolved. No data is ever lost.
2. **AI Model Drift Monitoring (Weekly)**: Data Scientists/Analysts review a sample of the `Alerts` generated by the AI to ensure the false-positive rate isn't drifting too high. They update the AI system prompt or model thresholds based on real-world feedback.
3. **Automated Incident Alerting**: If the API error rate exceeds 1%, or if the RabbitMQ queue depth exceeds 5,000 messages (indicating the Worker is too slow to keep up with transactions), an automated alert is sent to **PagerDuty** to immediately wake up the on-call engineer.
