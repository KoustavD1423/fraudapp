# AI Risk Scoring Agent Implementation Plan

This plan details exactly where to make crucial changes in your codebase to integrate the AI Risk Scoring Agent, while laying the groundwork for fault tolerance and Dead Letter Queues (DLQs).

## User Review Required
> [!IMPORTANT]
> Please review this plan. Adding AI involves changing the `TransactionIngested` event contract. If there are other services currently consuming this event, they will need to be recompiled or updated.
> Let me know if you approve these specific file changes!

## Open Questions
1. Do you want to use **Microsoft Semantic Kernel**, **OpenAI API directly**, or a **Local Model (Ollama)** for the AI risk evaluation implementation?
2. For fault tolerance, MassTransit automatically handles DLQs out of the box (moving failed messages to `transaction-queue_error`). Do you want to configure custom retry policies (e.g., exponential backoff) right now?

---

## Proposed Changes

### Fraud.Contracts Layer
This is the most critical change. To evaluate fraud, the AI agent needs more than just the `Amount`. It needs context like the `Merchant` and `AccountId`.

#### [MODIFY] [TransactionIngested.cs](file:///c:/Users/koust/Downloads/FraudPlatform/Fraud.Contracts/TransactionIngested.cs)
- Add `AccountId` and `Merchant` properties to the event contract so the Worker has enough data to pass to the AI model.

---

### Fraud.Application Layer
We need to abstract the AI logic so the Worker doesn't care *how* the score is calculated.

#### [NEW] `Fraud.Application/Interfaces/IRiskEvaluationService.cs`
- Create an interface with a method: `Task<double> EvaluateRiskAsync(TransactionIngested transaction);`

#### [MODIFY] [TransactionService.cs](file:///c:/Users/koust/Downloads/FraudPlatform/Fraud.Application/Services/TransactionService.cs)
- Update the `_eventBus.PublishAsync` call to include the newly added `AccountId` and `Merchant` properties from the `TransactionIngested` event.

---

### Fraud.Infrastructure Layer
This is where the actual AI/LLM integration lives.

#### [NEW] `Fraud.Infrastructure/Services/AiRiskEvaluationService.cs`
- Create the concrete implementation of `IRiskEvaluationService`.
- This file will contain the prompt logic (e.g., *"You are a fraud analyst. Review this transaction to merchant {Merchant}..."*) and make the external API call to your chosen AI provider.

---

### Fraud.Worker Layer
This is where we wire it all up and implement the actual decision logic.

#### [MODIFY] [TransactionConsumer.cs](file:///c:/Users/koust/Downloads/FraudPlatform/Fraud.Worker/TransactionConsumer.cs)
- **Remove**: `if (message.Amount > 1000)`
- **Add**: Inject `IRiskEvaluationService` via the constructor.
- **Add**: Call `var riskScore = await _riskService.EvaluateRiskAsync(context.Message);`
- **Add**: `if (riskScore > 0.8) { // create alert }`

#### [MODIFY] [Program.cs (Worker)](file:///c:/Users/koust/Downloads/FraudPlatform/Fraud.Worker/Program.cs)
- **Dependency Injection**: Register `services.AddScoped<IRiskEvaluationService, AiRiskEvaluationService>();`
- **Fault Tolerance (MassTransit)**: Add retry policies directly to the queue configuration. MassTransit handles DLQs automatically (sending to `_error` queues on failure).
  ```csharp
  busCfg.ReceiveEndpoint("transaction-queue", ep => {
      ep.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5))); // Fault tolerance
      ep.ConfigureConsumer<TransactionConsumer>(context);
  });
  ```

## Verification Plan

### Automated Tests
- Run `dotnet build` to ensure the new contract changes don't break the API.
- Add a unit test for `TransactionConsumer` mocking the `IRiskEvaluationService` to return `0.9` and verifying an `Alert` is saved.

### Manual Verification
- Run both the API and Worker locally.
- Use GraphQL to submit a transaction to a high-risk merchant.
- Verify the Worker logs the AI evaluation and creates an Alert in the database.
- Stop the worker, submit a transaction, and restart the worker to verify the message is persisted in RabbitMQ.
- Throw an intentional exception in the AI service to verify MassTransit retries the message 3 times before moving it to the DLQ (`transaction-queue_error`).
