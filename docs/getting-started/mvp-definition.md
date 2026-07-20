# MVP Definition: Geofenced Event Ticketing & Check-In

## MVP Goal
Deliver a fully functional, demo-ready system where an organizer can create an event with a geofence, an attendee can reserve and pay for a ticket, receive a QR ticket, and successfully check in on-site only when inside the allowed geofence.

## Demo-Ready Definition of Done
The MVP is complete when the end-to-end flow below can be executed reliably in a live environment (dev stack) without manual database edits.

1. Organizer creates an event with geofence center and radius.
2. Attendee reserves a ticket and gets a temporary `reserved` state with expiration.
3. Stripe test checkout succeeds and webhook marks ticket `paid`.
4. System generates a QR token and sends a notification (at least one channel).
5. Check-in endpoint accepts valid QR + in-geofence coordinates and marks ticket `checked-in`.
6. Repeated check-in attempts are safely rejected/idempotent.
7. Out-of-geofence check-in attempts are rejected with a clear reason.
8. After event end, scheduled no-show processor evaluates tickets and applies refund policy where configured.

## In-Scope Capabilities (MVP)

### 1) Event Management
- Create event (name, start/end, capacity, geofence center/radius).
- Get event details.
- Basic update support for non-destructive fields.

### 2) Ticket Lifecycle
- Reserve ticket endpoint.
- Reservation TTL expiration (`reserved -> expired`).
- State guardrails with conditional writes.

### 3) Payments Integration
- Stripe webhook endpoint with signature verification.
- Idempotent processing of duplicate webhook deliveries.
- Transition `reserved -> paid` exactly once.

### 4) QR + Notification
- QR token generation after payment confirmation.
- Delivery via one mandatory channel for MVP (Email or SMS).
- Delivery outcome logging for troubleshooting.

### 5) Geofenced Check-In
- Validate ticket exists and is in `paid` state.
- Validate the QR token from the scan matches the ticket.
- Validate the attendee device coordinates are within the event geofence.
- Validate the check-in occurs during the allowed event window.
- Transition `paid -> checked-in` with idempotency.

### 6) No-Show + Refund Automation
- EventBridge-triggered post-event reconciliation.
- Policy-based no-show evaluation and optional `paid -> refunded`.
- Audit log/event output for each decision.

### 7) Operational Baseline
- CloudWatch logs and core alarms.
- CI build + test execution.
- Basic runbook for webhook retry and check-in failures.

## Out of Scope (Post-MVP)
- Multi-zone geofences (VIP/backstage).
- Native mobile app offline mode.
- Advanced fraud heuristics.
- Deep analytics dashboard.
- Multi-tenant billing and enterprise RBAC.

## Functional Acceptance Criteria

### Event API
- Create event returns `201` with persisted geofence fields.
- Invalid geofence values return `400` with validation details.

### Reservation API
- Reserve ticket returns `201` and `status=reserved`.
- Capacity and expiration rules enforced under concurrency.

### Payment Webhook
- Valid Stripe signature required; invalid signature rejected.
- Duplicate event delivery does not double-transition state.

### Check-In API
- Valid paid ticket + matching QR token + in-geofence coordinates returns success and `checked-in`.
- Already checked-in ticket returns deterministic non-success response.
- Out-of-geofence location rejected with reason code.
- Check-in is allowed only when the backend validates the decision against the event geofence; the client only supplies coordinates.

### Refund Processor
- Runs on schedule after event end.
- Refunds only no-show paid tickets per policy.

## Editability and Change Control
- Event and ticket update operations use partial `PATCH` semantics.
- Edits are allowed after an entity exists, but each field is governed by status-based safeguards.
- Safe edits may be applied in place when they do not change entitlement, fulfillment, or check-in validity.
- High-impact edits that affect pricing, event assignment, or already-fulfilled tickets should require a deliberate decision path, such as refunding/canceling the current ticket and creating a replacement.
- No edits are allowed once a ticket is `checked-in` unless a specific recovery workflow is defined.
- All edits must be auditable and preserve the previous values for troubleshooting and abuse detection.

## Contract Design Baseline
- Required fields are the default for both ticketing and payment contracts.
- Ticket reservation requests must require the business identity fields needed to create a durable reservation, including event and attendee identity plus an idempotency key.
- Ticket metadata updates stay partial only where a field has a clear business reason to be optional.
- Payment events and payment records are separate from ticket lifecycle contracts.
- Payment-related identifiers, provider status, amounts, currency, and correlation fields belong to payment contracts rather than editable ticket fields.
- Payment records are append-only from the ticket perspective and should not expose raw payment method details as general ticket fields.

## Remaining Design Decisions
- Exact ticket fields that may be edited after reservation should stay limited until a clear business case is confirmed.
- Payment processing is Stripe-first for MVP, but the contract shape remains provider-aware so the payment record can expand later without changing ticket fields.
- Any future payment-method details should remain in provider-specific payment records, not in the general ticket contract.

## Non-Functional MVP Targets
- API p95 latency: under 500ms for read/validate paths (excluding external Stripe callback delays).
- Availability target (dev/demo): 99% during demo windows.
- Data integrity: all state transitions guarded by conditional writes.
- Auditability: every critical transition emits log with correlation ID.

## IAM Least-Privilege Baseline (Included in MVP)

### Principle
Each Lambda/service receives only the minimum AWS actions and resources required for its role, scoped by environment and resource ARN where possible.

### Service Permission Baseline

#### Events Service
- DynamoDB: `GetItem`, `PutItem`, `UpdateItem`, `Query` on `Events` table only.
- CloudWatch Logs: write logs.

#### Ticketing Service
- DynamoDB: `GetItem`, `PutItem`, `UpdateItem`, `Query` on `Tickets` table.
- Optional `Events` table read for capacity/geofence checks.
- CloudWatch Logs: write logs.

#### Payments Webhook Service
- DynamoDB: `GetItem`, `UpdateItem` on `Tickets` table.
- Secrets Manager or SSM Parameter Store read for Stripe webhook secret.
- CloudWatch Logs: write logs.

#### Notifications Service
- SNS: `Publish` to approved topic ARN(s) only.
- S3 (if QR image stored): `PutObject`, `GetObject` on QR bucket prefix only.
- DynamoDB read of ticket metadata if required.
- CloudWatch Logs: write logs.

#### Check-In Service
- DynamoDB: `GetItem`, `UpdateItem` on `Tickets`; `GetItem` on `Events`.
- CloudWatch Logs: write logs.

#### Refunds Service
- EventBridge trigger invocation permission.
- DynamoDB: `Query`, `UpdateItem` on `Tickets`.
- Payment provider secret read (if refund API call required).
- CloudWatch Logs: write logs.

### IAM Guardrails
- No wildcard actions (`*`) for data plane permissions.
- No wildcard resources unless AWS service requires it; document exceptions.
- Separate roles per service; no shared admin role.
- Explicit deny for non-required sensitive services when practical.
- Environment isolation (`dev`, `test`, `prod`) with separate roles/policies.

## Suggested MVP Milestone Sequence
1. Event APIs + schema + validation.
2. Ticket reservation + TTL + state guards.
3. Stripe webhook + paid transition idempotency.
4. QR issuance + one notification channel.
5. Geofence check-in and idempotent transition.
6. Event-end no-show/refund processor.
7. IAM tightening, alarms, runbooks, and full demo rehearsal.

## Demo Script (10–15 Minutes)
1. Create event with visible geofence settings.
2. Reserve ticket from attendee flow.
3. Complete Stripe test payment.
4. Show webhook processing and ticket state changes.
5. Show QR delivery evidence.
6. Attempt check-in outside geofence (fail).
7. Attempt check-in inside geofence (success).
8. Re-scan same QR (blocked/idempotent response).
9. Trigger/simulate post-event no-show reconciliation output.

## Exit Criteria for Moving Beyond MVP
- End-to-end demo passes twice consecutively in dev.
- Critical-path tests are green.
- IAM policies reviewed for least-privilege adherence.
- Known residual risks documented in runbooks/ADR.