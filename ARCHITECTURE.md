# Fraud Platform — Architecture

## 1. Architectural Style

The platform follows a Clean Architecture + event-driven architecture.

Primary communication patterns:
- Synchronous API requests
- Asynchronous RabbitMQ messaging
- MassTransit consumers
- SQL Server persistence
- Redis real-time intelligence

---

## 2. High-Level Architecture

```text
                         CLIENT
                           |
                           v
                    +-------------+
                    |  Fraud API  |
                    |  GraphQL    |
                    +------+------+
                           |
                           v
                +----------------------+
                | TransactionService   |
                +----------+-----------+
                           |
                           v
                     SQL Server
                           |
                           v
                 TransactionIngested
                           |
                           v
                 +----------------+
                 |    RabbitMQ    |
                 +-------+--------+
                         |
                         v
                 +---------------+
                 | MassTransit   |
                 | Worker        |
                 +-------+-------+
                         |
                         v
              FraudProcessingService
                         |
              +----------+----------+
              |                     |
              v                     v
        +-----------+          +---------+
        | RuleEngine|          |  Redis  |
        +-----+-----+          +----+----+
              |                     |
              +----------+----------+
                         |
                         v
                    Risk Signals
                         |
                         v
                      Alert
                         |
                         v
                    SQL Server
```

---

## 3. Project Structure

```text
FraudPlatform/
│
├── Fraud.Api/
│   ├── GraphQL/
│   ├── Middleware/
│   └── Services/
│
├── Fraud.Application/
│   ├── Context/
│   ├── Data/
│   ├── Factories/
│   ├── Interfaces/
│   ├── Rules/
│   └── Services/
│
├── Fraud.Contracts/
│   └── TransactionIngested.cs
│
├── Fraud.Domain/
│   ├── Transaction.cs
│   └── Alert.cs
│
├── Fraud.Infrastructure/
│   ├── Repositories/
│   └── Messaging/
│
├── Fraud.Worker/
│   └── TransactionConsumer.cs
│
└── Fraud.Tests/
    ├── Unit/
    ├── Integration/
    └── Context/
```

---

## 4. API Layer

**Responsibilities:**
- Receive transactions
- GraphQL/API endpoints
- Request validation
- Middleware
- Correlation IDs
- Exception handling

The API does not perform fraud analysis directly.

---

## 5. Application Layer

Contains business/application logic.

**Important components:**

**`TransactionService`**
Creates transactions and publishes transaction events.

**`FraudProcessingService`**
Coordinates fraud processing.

Responsibilities:
- Idempotency
- Rule evaluation
- Alert creation

**`RuleEngine`**
Executes registered fraud rules.

---

## 6. Domain Layer

Contains core domain entities.

**Current entities:**
- `Transaction`
- `Alert`

The domain remains independent of RabbitMQ, Redis, GraphQL, and infrastructure implementations.

---

## 7. Messaging

RabbitMQ + MassTransit provide asynchronous transaction processing.

```text
API
 |
 | TransactionIngested
 v
RabbitMQ
 |
 v
TransactionConsumer
 |
 v
FraudProcessingService
```

Correlation identifiers are propagated through the message pipeline.

---

## 8. Fraud Rule Engine

Rules implement the asynchronous fraud-rule contract. Current rules include:
- `HighAmountRule`
- `VeryHighAmountRule`
- `VelocityRule`

The rule engine aggregates matching rules into:
- Fraud decision
- Risk score
- Human-readable reasons

Velocity evaluation provides detailed account, device, and IP counts and exceeded-dimension signals. Those details are translated into risk reasons for persisted fraud alerts.

## 9. Idempotency and Durable Processing Outcomes

Reliability is implemented at both the message-processing and business-operation layers.

**Producer outbox**

The API registers MassTransit EF Core Bus Outbox with the same `ApplicationDbContext` used to persist the transaction. The transaction and outgoing `TransactionIngested` event are saved in one database commit. If RabbitMQ is unavailable, the event remains in the SQL outbox for later delivery.

**Consumer inbox/outbox**

The worker transaction consumer uses the EF Core Consumer Outbox/Inbox. The inbox tracks message identifiers so a repeated message ID is not processed as a new delivery.

**Business idempotency**

A transaction that has already reached `Completed` is not re-evaluated when another message ID refers to the same business transaction. Processing validates the incoming payload against the persisted transaction. An existing alert can be used to recover a missing/incomplete durable outcome.

**Durable outcome**

The `Transaction` record stores processing status, fraud decision, risk score, fraud reasons, processing timestamp, and a SQL Server row-version token. The processing outcome and optional alert are saved together. A unique index on `Alert.TransactionId`, along with concurrency handling, protects against duplicate alerts.

Message Inbox deduplication and business transaction idempotency are separate controls: different message IDs can reach the consumer while still producing no repeated business effects.

## 10. Reliability and Failure Recovery

Current reliability mechanisms include:
- SQL Server and RabbitMQ health checks
- Structured logging and correlation IDs
- MassTransit EF Core producer Bus Outbox
- Consumer Inbox/Outbox
- Application-level transaction idempotency
- Unique database constraint on `Alert.TransactionId`
- SQL Server row-version concurrency handling
- Retry handling for transient consumer failures
- Redis per-transaction/dimension deduplication to avoid recounting successful dimensions on retry
- Automated unit and integration tests for concurrency and failure recovery

Integration tests require configured SQL Server, Redis, and RabbitMQ services. Local test configuration can be supplied through `FRAUD_TEST_SQLSERVER`, `FRAUD_TEST_REDIS`, and the standard .NET connection-string environment variables.

## 11. Redis — Sprint 2

Redis provides short-lived velocity signals. It is not the source of truth for transactions; SQL Server remains the durable transactional store.

**Dimensions**
- Account velocity
- Device velocity
- IP velocity

**Counter semantics**
- Each dimension uses a configurable time window.
- Counter expiry is fixed from the initial increment rather than extended by every subsequent transaction.
- Thresholds and window length are configured through `VelocityOptions`.
- Redis Lua operations make counter/deduplication updates atomic.
- Each transaction/dimension has a deduplication key with a configured retention period, so retries do not increment a dimension that already succeeded.
- The implementation supports partial failure: successful dimensions are deduplicated if another dimension fails and processing retries.

**Evaluation flow**

```text
TransactionIngested
       |
       v
FraudProcessingService
       |
       v
    RuleEngine
       |
       +---- Amount rules
       |
       +---- VelocityRule
                 |
          +------+------+
          |      |      |
        Account Device  IP
          |      |      |
          +------+------+
                 |
        Detailed velocity result
                 |
                 v
        Fraud decision/reasons
                 |
                 v
       SQL Server outcome + alert
```

## 12. Future Feature Store

The feature store will build on Redis and historical SQL data.

**Expected features:**
- Account behavior
- Device behavior
- IP behavior
- Merchant behavior
- Geographic behavior
- Transaction velocity
- Historical fraud patterns

---

## 13. ML Scoring

**Future architecture:**

```text
Feature Store
      |
      v
ML Scoring Service
      |
      v
Risk Probability
      |
      v
Fraud Decision
```

---

## 14. Vector Search

Vector search will represent transaction/entity behavior as embeddings.

**Potential uses:**
- Similar transaction detection
- Fraud ring discovery
- Entity relationships
- Historical case similarity
- Investigator search

---

## 15. AI Agents

**Future agent architecture:**

```text
                    Fraud Platform
                         |
                         v
                  AI Scout Agent
                         |
                         v
                 Evidence Retrieval
                         |
                         v
                  AI Analyst Agent
                         |
                         v
                Investigation Result
                         |
                         v
                Resolution Agent
```

Agents will consume existing fraud intelligence rather than bypassing core platform controls.

---

## 16. Camunda BPMN

Camunda will be introduced as a dedicated business workflow orchestration layer.

It will coordinate processes such as:

```text
Alert
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

Camunda is responsible for workflow orchestration, not replacing the fraud rule engine or ML services.

---

## 17. Observability

**Future production observability:**
- Distributed tracing
- Metrics
- Logs
- Service dashboards
- Alerting

Correlation IDs established in the current platform will provide the foundation for distributed tracing.

---

## 18. Security

**Future security capabilities:**
- Authentication
- Authorization
- PII masking
- Secrets management
- API protection
- Audit logging
- Service-to-service security

---

## 19. Production Deployment

**Final target:**

```text
GitHub
   |
   v
CI/CD
   |
   v
Docker
   |
   v
Cloud Environment
   |
   +---- Fraud API
   +---- Fraud Worker
   +---- RabbitMQ
   +---- SQL Server
   +---- Redis
   +---- ML Service
   +---- Vector Store
   +---- Camunda
   +---- Observability
```