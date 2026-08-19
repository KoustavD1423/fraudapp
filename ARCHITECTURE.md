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

Rules implement `IFraudRule`.

**Current rules:**
- `HighAmountRule`
- `VeryHighAmountRule`

The `RuleEngine` evaluates all registered rules and produces:
- `IsFraud`
- `RiskScore`
- `Reasons`

---

## 9. Idempotency

The platform currently uses two layers.

**Application protection**

```text
Check TransactionId
        |
        +-- exists --> ignore
        |
        +-- missing --> process
```

**Database protection**

`Alert.TransactionId` has a unique database index.

This provides a final protection against duplicate alerts caused by concurrent processing.

---

## 10. Reliability

**Current reliability mechanisms:**
- Health checks
- RabbitMQ configuration
- SQL Server health monitoring
- Correlation IDs
- Structured logging
- Global exception middleware
- Application idempotency
- Database uniqueness constraint
- Automated tests

---

## 11. Redis — Sprint 2

Redis will provide short-lived, real-time fraud intelligence.

**Initial use cases:**
- Account velocity
- Device velocity
- IP velocity
- Transaction frequency
- Time-window counters

**Example:**

```text
Account A
   |
   +-- Transaction 1
   +-- Transaction 2
   +-- Transaction 3
   +-- Transaction 4
   |
   v
Redis
   |
   v
"4 transactions in 60 seconds"
   |
   v
Velocity Risk Signal
```

Redis is not the source of truth for transactions.

SQL Server remains the durable transactional store.

---

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