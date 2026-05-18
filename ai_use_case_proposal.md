# AI-Powered Fraud Resolution Use Case: "Autonomous E-Commerce Payment Shield"

This use case demonstrates how the `FraudPlatform` can be evolved from a simple alerting system into an end-to-end **autonomous fraud mitigation engine** for an e-commerce or fintech business.

## The Business Problem
Currently, most fraud detection platforms generate hundreds of alerts per day based on rigid rules (e.g., amount > $1000). Human analysts must manually review each alert by looking at the user's history, checking IP addresses, and contacting the customer. This process is slow (often taking hours), expensive, and leads to delayed legitimate transactions (customer friction) or successful fraud.

## The AI-Automated Pipeline

By leveraging the existing MassTransit/RabbitMQ architecture in `FraudPlatform`, we can chain together AI agents that act entirely autonomously.

### Step 1: Real-Time Scoring (The "Scout" Agent)
**Action**: A user makes a $1,500 purchase for a high-end laptop at 3 AM.
1. The API receives the transaction and publishes the `TransactionIngested` event.
2. The **Scout Agent** (a new consumer in the Worker) instantly retrieves context:
   - *Device/IP*: The IP is in a different country from the user's billing address.
   - *History*: The user has never spent over $200 in a single transaction.
3. The Agent uses an ML model (or lightweight LLM prompt) to assign a **Risk Score of 0.92**. It saves an `Alert` to the database and publishes an `AlertCreated` event.

### Step 2: Autonomous Triage & Review (The "Analyst" Agent)
**Action**: Instead of queuing the alert for a human, the **Analyst Agent** (an LLM-powered consumer) immediately picks up the `AlertCreated` event.
1. It queries the database for the user's full transaction history, login history, and KYC data.
2. It reasons over the data (acting like a human investigator): 
   > *"The purchase is out of state, the amount is 7x the historical average, and the item (electronics) is a high-risk category for reselling. However, the user's login is from a historically recognized mobile device."*
3. The Analyst Agent attaches an **Investigation Summary** to the Alert record, detailing exactly *why* this looks suspicious.

### Step 3: Proposing & Executing Solutions (The "Resolution" Agent)
**Action**: Based on the Analyst Agent's summary and risk score, the **Resolution Agent** takes immediate action. It selects from a predefined set of solutions:
- **Low Risk (Score 0.5 - 0.7)**: Automatically send an SMS to the user asking "Did you make this purchase?". Hold the transaction pending their reply.
- **Medium Risk (Score 0.7 - 0.9)**: Trigger Step-Up Authentication (MFA) via the app before the payment clears.
- **High Risk (Score > 0.9)**: **(Our Current Scenario)** 
   1. The Resolution Agent automatically **Declines** the transaction via an API call.
   2. It temporarily **Freezes** the account/card to prevent further attempts.
   3. It sends an email to the user with a secure link to verify their identity and unfreeze the account.
   4. It marks the `Alert` status in the database as `Auto-Mitigated`.

## Business Impact & Feasibility
Implementing this within `FraudPlatform` is highly feasible because the event-driven backbone (MassTransit) is already in place. You just need to add new Consumers for each Agent.

**The Value proposition:**
- **Cost Reduction**: Automates 80-90% of manual alert reviews. Human fraud analysts only intervene on edge cases or customer appeals.
- **Speed**: The entire AI pipeline (Scoring -> Review -> Resolution) executes in seconds, preventing the money from leaving the system.
- **Customer Experience**: Legitimate customers flagged by mistake get an instant SMS to approve the charge, rather than having their card blocked silently and waiting on hold with customer service.
