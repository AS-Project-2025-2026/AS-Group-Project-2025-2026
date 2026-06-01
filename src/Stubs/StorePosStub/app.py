import asyncio
import base64
import datetime as dt
import json
import os
import uuid
import urllib.error
import urllib.request
from typing import Any

from fastapi import Body, FastAPI, status
from fastapi.responses import JSONResponse

app = FastAPI(title="VerdeMart Store POS Stub")

_store_stock: dict[int, int] = {
    1: 5,
    2: 3,
    3: 0,
}


def current_mode() -> str:
    return os.getenv("STUB_MODE", "normal").strip().lower()


def delay_seconds() -> float:
    raw = os.getenv("STUB_DELAY_MS", "0").strip()
    try:
        ms = int(raw)
    except ValueError:
        ms = 0
    return max(ms, 5000) / 1000


def rabbitmq_publish(payload: dict[str, Any]) -> dict[str, Any]:
    base_url = os.getenv("RABBITMQ_MANAGEMENT_URL", "http://localhost:15672").rstrip("/")
    exchange = os.getenv("RABBITMQ_EXCHANGE", "verdemart.integration")
    routing_key = os.getenv("POS_STOCK_ROUTING_KEY", "pos.stock.reported")
    username = os.getenv("RABBITMQ_USERNAME", "guest")
    password = os.getenv("RABBITMQ_PASSWORD", "guest")

    body = json.dumps(
        {
            "properties": {"delivery_mode": 2},
            "routing_key": routing_key,
            "payload": json.dumps(payload),
            "payload_encoding": "string",
        }
    ).encode("utf-8")

    token = base64.b64encode(f"{username}:{password}".encode("utf-8")).decode("ascii")
    request = urllib.request.Request(
        f"{base_url}/api/exchanges/%2F/{exchange}/publish",
        data=body,
        method="POST",
        headers={
            "Authorization": f"Basic {token}",
            "Content-Type": "application/json",
        },
    )

    with urllib.request.urlopen(request, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


@app.get("/health")
async def health() -> dict[str, Any]:
    return {"status": "ok", "service": "storepos_stub", "mode": current_mode()}


@app.post("/pickup")
async def confirm_pickup(payload: dict[str, Any] | None = Body(default=None)) -> JSONResponse:
    """
    Simulates the store POS confirming that a pickup order is ready for collection.
    """
    payload = payload or {}
    mode = current_mode()
    correlation_id = payload.get("correlationId")
    order_id = payload.get("orderId", "unknown")

    if mode == "slow":
        await asyncio.sleep(delay_seconds())

    if mode == "offline":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={
                "status": "unavailable",
                "reason": "storepos_simulated_offline",
                "correlationId": correlation_id,
            },
        )

    pickup_ref = f"POS-{order_id}"
    return JSONResponse(
        status_code=status.HTTP_200_OK,
        content={
            "status": "pickup_confirmed",
            "pickupReference": pickup_ref,
            "storeLocation": "VerdeMart Main Store",
            "correlationId": correlation_id,
        },
    )


@app.post("/stock/report")
async def report_stock(payload: dict[str, Any] | None = Body(default=None)) -> JSONResponse:
    """Publishes a POS-originated stock report to the integration exchange."""
    payload = payload or {}
    mode = current_mode()

    if mode == "slow":
        await asyncio.sleep(delay_seconds())

    if mode == "offline":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={"status": "unavailable", "reason": "storepos_simulated_offline"},
        )

    product_id = payload.get("productId")
    quantity = payload.get("quantity")

    if not isinstance(product_id, int) or product_id <= 0:
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content={"status": "error", "reason": "productId must be a positive integer"},
        )

    if not isinstance(quantity, int) or quantity < 0:
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content={"status": "error", "reason": "quantity must be a non-negative integer"},
        )

    correlation_id = payload.get("correlationId") or str(uuid.uuid4())
    idempotency_key = payload.get("idempotencyKey") or f"pos-stock-{product_id}-{correlation_id}"
    event = {
        "productId": product_id,
        "quantity": quantity,
        "sourceSystem": "pos",
        "location": payload.get("location", "VerdeMart Main Store"),
        "correlationId": correlation_id,
        "idempotencyKey": idempotency_key,
        "reportedAtUtc": dt.datetime.now(dt.UTC).isoformat(),
    }

    try:
        publish_result = rabbitmq_publish(event)
    except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as exc:
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={
                "status": "publish_failed",
                "reason": str(exc),
                "correlationId": correlation_id,
            },
        )

    _store_stock[product_id] = quantity
    return JSONResponse(
        content={
            "status": "reported",
            "productId": product_id,
            "posQuantity": quantity,
            "routingKey": os.getenv("POS_STOCK_ROUTING_KEY", "pos.stock.reported"),
            "routed": publish_result.get("routed", False),
            "correlationId": correlation_id,
        }
    )


@app.get("/store-stock")
async def get_store_stock() -> JSONResponse:
    """Returns in-store stock levels (independent from warehouse stock)."""
    mode = current_mode()

    if mode == "offline":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={"status": "unavailable", "reason": "storepos_simulated_offline"},
        )

    store_stock = [
        {"productId": product_id, "quantity": quantity, "location": "VerdeMart Main Store"}
        for product_id, quantity in sorted(_store_stock.items())
    ]
    return JSONResponse(content={"status": "ok", "storeStock": store_stock})
