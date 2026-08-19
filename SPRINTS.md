# Fraud Platform — Sprint Roadmap

## Project Objective
Build a production-oriented fraud detection platform using event-driven architecture, real-time fraud intelligence, ML scoring, vector search, AI agents, workflow orchestration, observability, security, and CI/CD.

---

## Sprint Status

| Sprint | Name | Outcome | Status |
|---:|---|---|---|
| 0 | Foundation | Clean architecture baseline | ✅ Complete |
| 1 | Messaging Core | RabbitMQ + MassTransit + reliability | ✅ Complete |
| 2 | Fraud Intelligence | Rule engine + Redis velocity | 🟡 In Progress |
| 3 | Feature Store | Real-time feature engineering | ⏳ Planned |
| 4 | ML Scoring Service | Risk model inference | ⏳ Planned |
| 5 | Vector Search | Similarity and fraud rings | ⏳ Planned |
| 6 | Search API | Investigator search | ⏳ Planned |
| 7 | Real-Time Dashboard | Live alert delivery | ⏳ Planned |
| 8 | Case Management | Alerts to cases | ⏳ Planned |
| 9 | AI Analyst Agent | RAG explanations | ⏳ Planned |
| 10 | Resolution Agent | Automated actions | ⏳ Planned |
| 11 | Camunda BPMN | Workflow orchestration | ⏳ Planned |
| 12 | Observability | Traces and metrics | ⏳ Planned |
| 13 | Security | Auth, PII masking | ⏳ Planned |
| 14 | Productionization | CI/CD, load tests, release | ⏳ Planned |

---

# Sprint 0 — Foundation

### Status: COMPLETE

### Completed
- Clean Architecture structure
- Domain layer
- Application layer
- Infrastructure layer
- API layer
- Worker layer
- Contracts
- SQL Server / Entity Framework Core
- Repository abstraction
- GraphQL API foundation
- Dependency Injection

### Outcome
Clean architecture baseline established.

---

# Sprint 1 — Messaging Core

### Status: COMPLETE

### Completed
- RabbitMQ
- MassTransit
- `TransactionIngested` contract
- Transaction event publishing
- Worker consumer
- Correlation IDs
- Message metadata
- Transaction event factory
- RabbitMQ configuration
- Strongly typed RabbitMQ configuration
- Basic messaging reliability

### Outcome
Transaction ingestion became event-driven.

```text
API
 ↓
TransactionService
 ↓
RabbitMQ
 ↓
MassTransit
 ↓
Fraud Worker
```

---

# Sprint 2 — Fraud Intelligence

### Status: IN PROGRESS

### Completed

**Platform Reliability**
- Health checks
  - SQL Server health check
  - RabbitMQ health check
  - `IOptions<RabbitMqOptions>`
- Global exception middleware
- Correlation ID handling
- Structured Serilog logging

**Fraud Processing**
- `IFraudRule`
- `HighAmountRule`
- `VeryHighAmountRule`
- `RuleEngine`
- `FraudProcessingService`
- Fraud alert persistence

**Idempotency**
- Application-level duplicate detection
- Database unique constraint on `Alert.TransactionId`
- Idempotency unit tests

**Testing**
- Rule engine tests
- Transaction event factory tests
- Context tests
- Fraud processing tests
- Idempotency tests
- API → RabbitMQ → Worker verification
- Middleware runtime verification

### Remaining

**Redis Fraud Velocity**

Implement real-time transaction velocity detection using Redis.

Initial capabilities:
- Transaction count by account
- Transaction count by device
- Transaction count by IP
- Time-window based velocity
- Short-term transaction frequency
- Redis TTL
- Velocity-based risk signals
- Integration with fraud processing

### Target Architecture

```text
Transaction
     |
     v
  RabbitMQ
     |
     v
 Fraud Worker
     |
     v
FraudProcessingService
     |
     +------------+
     |            |
     v            v
Rule Engine     Redis
     |         Velocity
     |            |
     +-----+------+
           |
           v
      Risk Signals
           |
           v
        Alert
```

---

# Sprint 3 — Feature Store

### Status: PLANNED

Build a real-time feature engineering layer.

**Expected features:**
- Account velocity
- Device velocity
- IP velocity
- Transaction frequency
- Merchant statistics
- Historical behavior
- Geographic features
- Device/account relationships

---

# Sprint 4 — ML Scoring Service

### Status: PLANNED

Introduce machine-learning based risk scoring.

**Expected components:**
- Feature ingestion
- Feature transformation
- Model inference
- Risk probability
- Model versioning
- ML scoring API/service

> The initial AI Scout capability can begin around this stage.

---

# Sprint 5 — Vector Search

### Status: PLANNED

Introduce vector representations for:
- Transactions
- Accounts
- Devices
- Merchants
- Fraud patterns

Use vector similarity to identify related behavior and potential fraud rings.

---

# Sprint 6 — Search API

### Status: PLANNED

Provide investigator-facing search capabilities.

---

# Sprint 7 — Real-Time Dashboard

### Status: PLANNED

Provide:
- Live alerts
- Risk scores
- Transaction details
- Fraud reasons
- Velocity signals
- Investigation visibility

---

# Sprint 8 — Case Management

### Status: PLANNED

Convert fraud alerts into investigation cases.

---

# Sprint 9 — AI Analyst Agent

### Status: PLANNED

Introduce RAG-based fraud investigation and explanations.

**Responsibilities:**
- Analyze alerts
- Retrieve relevant evidence
- Explain risk factors
- Summarize transaction history
- Recommend investigation actions

---

# Sprint 10 — Resolution Agent

### Status: PLANNED

Introduce controlled automated fraud-response actions.

**Examples:**
- Approve
- Request additional verification
- Hold transaction
- Escalate
- Decline

> Actions must remain policy-controlled.

---

# Sprint 11 — Camunda BPMN

### Status: PLANNED

Introduce business process orchestration.

Camunda will orchestrate workflows such as:

```text
Fraud Alert
    |
    v
Risk Evaluation
    |
    +---- Low Risk ------> Approve
    |
    +---- Medium Risk ---> Verification
    |
    +---- High Risk -----> Investigation
                              |
                              v
                         Resolution
```

---

# Sprint 12 — Observability

### Status: PLANNED

Introduce production-grade:
- Distributed tracing
- Metrics
- Dashboards
- Alerting
- Service health
- Performance monitoring

---

# Sprint 13 — Security

### Status: PLANNED

Introduce:
- Authentication
- Authorization
- PII masking
- Secrets management
- API security
- Service-to-service security
- Audit logging

---

# Sprint 14 — Productionization

### Status: PLANNED

Introduce:
- Docker
- GitHub Actions CI/CD
- Automated tests
- Security scanning
- Integration tests
- Load testing
- Deployment pipelines
- Environment configuration
- Release strategy
