# Geofenced Event Ticketing & Check-In System

## Overview
A mobile-first event platform where organizers create events with geofenced check-in zones, attendees purchase tickets, and on-site staff validate entry using QR scan plus real-time location checks.

This project models real SaaS complexity: payment webhooks, ticket state transitions, geolocation validation, async event processing, and refund automation.

## Why This Project Stands Out
- Real-world product flow similar to Eventbrite-style systems.
- Combines external payments (Stripe) with AWS-native event-driven design.
- Demonstrates robust state management and security for ticket lifecycle.
- Includes location-aware authorization logic at check-in.

## Core Features
1. **Organizer Event Management**
   - Create/update events with date/time, venue, capacity, and geofence coordinates.
2. **Ticket Purchase Flow**
   - Reserve tickets, process payment through Stripe, confirm via webhook.
3. **Digital Ticket Delivery**
   - Generate QR codes and deliver via SNS/Email/SMS.
4. **Geofenced Check-In API**
   - Validate attendee location and ticket status before check-in.
5. **Automated No-Show Handling**
   - EventBridge schedules post-event processing and optional refunds.

## Suggested AWS Architecture
- **Amazon API Gateway**: Public APIs for organizer/admin/mobile clients.
- **AWS Lambda**: Business logic for event creation, purchase, webhook processing, and check-in.
- **Amazon DynamoDB**: Event, ticket, and attendee records with state machine transitions.
- **Amazon EventBridge**: Time-based and domain events (purchase paid, event ended, no-show processing).
- **Amazon SNS**: Ticket delivery notifications (SMS/email fan-out patterns).
- **Amazon S3**: Store QR code images and optional event assets.
- **AWS WAF + API Gateway Auth (JWT/Cognito)**: API protection and identity.
- **CloudWatch Logs/Metrics**: Observability and operational alerts.

## Ticket State Machine
`reserved -> paid -> checked-in -> expired`

Optional branches:
- `reserved -> expired` (payment timeout)
- `paid -> refunded` (no-show/refund policy)
- `paid -> cancelled` (manual organizer cancellation)

## High-Level Flow
1. Organizer creates event and geofence.
2. Attendee reserves ticket (temporary hold + TTL).
3. Stripe Checkout completes payment.
4. Stripe webhook triggers Lambda to mark ticket `paid`.
5. QR code is generated and sent through SNS/email.
6. At venue, mobile app scans QR and sends device location.
7. Check-in API validates:
   - ticket exists and is `paid`
   - ticket not already checked-in/expired
   - location within geofence radius
8. Ticket transitions to `checked-in`.
9. After event end, EventBridge triggers no-show/refund evaluation.

## Data Model (DynamoDB)
### Table: `Tickets`
- **PK**: `EVENT#{eventId}`
- **SK**: `TICKET#{ticketId}`
- Attributes:
  - `attendeeId`
  - `status` (`reserved|paid|checked-in|expired|refunded|cancelled`)
  - `reservedAt`, `paidAt`, `checkedInAt`, `expiresAt`
  - `qrToken`
  - `stripeSessionId`, `stripePaymentIntentId`
  - `geofenceValidation` (lastLat, lastLon, distanceMeters, validatedAt)

### Table: `Events`
- **PK**: `ORG#{organizerId}`
- **SK**: `EVENT#{eventId}`
- Attributes:
  - `name`, `startTime`, `endTime`, `capacity`
  - `geofenceCenterLat`, `geofenceCenterLon`, `geofenceRadiusMeters`

## API Endpoints (Example)
- `POST /events` - create event
- `GET /events/{eventId}` - get event
- `POST /events/{eventId}/tickets/reserve` - reserve ticket
- `POST /payments/stripe/webhook` - Stripe webhook callback
- `GET /tickets/{ticketId}` - ticket details
- `POST /checkin` - validate QR + geolocation and check in

## Security & Reliability Notes
- Verify Stripe webhook signatures.
- Use idempotency keys for webhook and check-in operations.
- Enforce conditional writes in DynamoDB for valid state transitions.
- Store minimal PII and encrypt sensitive fields.
- Add replay protection for QR tokens (single-use or short-lived signed tokens).

## MVP Milestones
1. Event CRUD + geofence definition.
2. Ticket reservation with TTL expiry.
3. Stripe webhook-based payment confirmation.
4. QR generation + notification delivery.
5. Geofence check-in endpoint with state transition guards.
6. EventBridge no-show/refund automation.

## Nice-to-Have Extensions
- Multi-geofence zones (VIP, backstage, general entry).
- Fraud detection (impossible travel/time anomalies).
- Offline-first scanner mode with deferred sync.
- Organizer analytics dashboard (conversion, show rate, check-in peak times).

## Local Development Notes (Visual Studio + AWS Toolkit)
- Build Lambdas in .NET with Visual Studio and AWS Toolkit.
- Use AWS SAM for local API/Lambda testing.
- Use CloudWatch logs integration to troubleshoot webhook/check-in flows.

## Elevator Pitch
A production-style, cloud-native ticketing platform that proves advanced backend skills: event-driven architecture, payment integration, geospatial access control, and stateful workflow orchestration on AWS.