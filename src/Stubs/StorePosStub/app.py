import asyncio
import os
from typing import Any

from fastapi import Body, FastAPI, status
from fastapi.responses import JSONResponse

app = FastAPI(title="VerdeMart Store POS Stub")


def current_mode() -> str:
    return os.getenv("STUB_MODE", "normal").strip().lower()


def delay_seconds() -> float:
    raw = os.getenv("STUB_DELAY_MS", "0").strip()
    try:
        ms = int(raw)
    except ValueError:
        ms = 0
    return max(ms, 5000) / 1000


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


@app.get("/store-stock")
async def get_store_stock() -> JSONResponse:
    """Returns in-store stock levels (independent from warehouse stock)."""
    mode = current_mode()

    if mode == "offline":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={"status": "unavailable", "reason": "storepos_simulated_offline"},
        )

    # Static in-store stock for demo purposes
    store_stock = [
        {"productId": 1, "quantity": 5, "location": "Aisle-A"},
        {"productId": 2, "quantity": 3, "location": "Aisle-B"},
        {"productId": 3, "quantity": 0, "location": "Aisle-A"},
    ]
    return JSONResponse(content={"status": "ok", "storeStock": store_stock})
