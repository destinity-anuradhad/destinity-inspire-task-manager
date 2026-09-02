# Webhooks

Webhooks deliver **signed HTTP callbacks** to a team's registered endpoints when task events occur.
They complement the [v1 API](api-v1.md): the API is how integrations *read/write*, webhooks are how
they *react*.

## Events

| Event | Fires when |
|---|---|
| `task.created` | A task is created |
| `task.status_changed` | A task's status changes (quick action, board, or full edit) |
| `task.assigned` | A task's owner changes |
| `task.comment_added` | A comment is added to a task |

A subscription stores a subset of these (or `*` for all) and only receives matching events for its
own team.

## Managing subscriptions

Team administrators and system administrators manage endpoints under **Admin → Webhooks**
(`/Webhooks`): add an endpoint (name + payload URL + events), pause/enable, delete, and view recent
deliveries with a **Replay** action for dead-lettered/canceled deliveries. The signing **secret** is
shown once at creation and stored to sign every delivery.

## Payload

Each delivery is a `POST` with a JSON body:

```json
{
  "id": "0f1e2d…",
  "type": "task.status_changed",
  "createdAt": "2026-09-02T09:40:00Z",
  "teamId": 1,
  "actor": "Anuradha",
  "data": { "id": 42, "goal": "…", "status": "Done", "priority": "High", "owner": "…", "teamId": 1 }
}
```

### Headers

| Header | Meaning |
|---|---|
| `X-TaskTracker-Event` | The event type |
| `X-TaskTracker-Delivery` | Unique delivery id (echo of `id`) — use for idempotency |
| `X-TaskTracker-Timestamp` | Unix seconds when the request was signed |
| `X-TaskTracker-Signature` | `sha256=<hex>` HMAC of `"{timestamp}.{rawBody}"` using the secret |

### Verifying (pseudocode)

```
expected = "sha256=" + hex(hmac_sha256(secret, timestamp + "." + rawBody))
reject if expected != X-TaskTracker-Signature
reject if abs(now - timestamp) > tolerance   # replay protection
```

## Delivery semantics

- **At-least-once.** Deliveries are persisted to a durable outbox; a background sender POSTs them.
- **Success** = any `2xx` within 15s. Anything else (non-2xx, timeout, DNS/TLS/connection error) is
  a failure.
- **Retries** use exponential backoff (`2^attempt` minutes, capped at 60) up to 6 attempts, after
  which the delivery is **dead-lettered** and can be replayed from the UI.
- **Ordering is not guaranteed** — use the payload `createdAt`/`id` if you need to order events.
- Deactivating or deleting a subscription cancels its pending deliveries.

## Security notes

- Always verify the signature and reject stale timestamps before trusting a payload.
- Payload URLs are not restricted to public hosts, so in a hardened deployment consider egress
  filtering to prevent SSRF to internal addresses (a follow-up hardening item).
