# API Documentation Structure

This folder contains the modular OpenAPI contract for the MVP.

## Layout

- `openapi.yaml` - root OpenAPI entrypoint
- `paths/` - path fragments grouped by area
  - `events/`
  - `tickets/`
  - `payments/`
- `components/` - shared schema fragments grouped by area
  - `schemas.yaml` - schema index
  - `schemas/events.yaml`
  - `schemas/tickets.yaml`
  - `schemas/payments.yaml`

## Notes

- Keep the root file small.
- Add new endpoints to the matching path fragment.
- Add new schemas to the matching domain schema file and expose them through `components/schemas.yaml`.
- Preserve the existing contract names when refactoring so `$ref` links remain stable.
