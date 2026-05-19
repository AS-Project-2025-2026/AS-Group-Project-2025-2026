# Integration Stubs

Python/FastAPI stubs used by the omnichannel integration spike.

## Services

- Warehouse Stub: `http://localhost:5081`
- Shipping Stub: `http://localhost:5082`
- FastAPI docs: `/docs` on each service

## Modes

Warehouse:

- `WAREHOUSE_STUB_MODE=normal`: `POST /fulfillment` returns `202 picked`.
- `WAREHOUSE_STUB_MODE=slow` or `delayed`: waits `WAREHOUSE_STUB_DELAY_MS`, or 15000 ms when the delay is `0` or invalid, then returns success.
- `WAREHOUSE_STUB_MODE=failed`: returns `500 warehouse_simulated_failure`.

Shipping:

- `SHIPPING_STUB_MODE=normal`: `POST /labels` returns `201 label_created`.
- `SHIPPING_STUB_MODE=slow`: waits `SHIPPING_STUB_DELAY_MS`, or 15000 ms when the delay is `0` or invalid, then returns success.
- `SHIPPING_STUB_MODE=outage`: returns `503 shipping_simulated_outage`.

## Quick Checks

```bash
curl http://localhost:5081/health
curl http://localhost:5082/health

curl -i -X POST http://localhost:5081/fulfillment \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","items":[{"sku":"SKU-1","quantity":2}]}'

curl -i -X POST http://localhost:5082/labels \
  -H "Content-Type: application/json" \
  -d '{"orderId":123,"correlationId":"order-123","idempotencyKey":"test-key","recipient":{"name":"Customer","address":"Address"}}'
```
