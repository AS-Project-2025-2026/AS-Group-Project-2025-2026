import asyncio
import os
from typing import Any

from fastapi import Body, FastAPI, status
from fastapi.responses import JSONResponse


app = FastAPI(title="VerdeMart Shipping Stub")


def current_mode() -> str:
    return os.getenv("STUB_MODE", "normal").strip().lower()


def delay_seconds() -> float:
    raw_delay = os.getenv("STUB_DELAY_MS", "0").strip()
    try:
        delay_ms = int(raw_delay)
    except ValueError:
        delay_ms = 0

    if delay_ms <= 0:
        delay_ms = 15000

    return delay_ms / 1000


@app.get("/health")
async def health() -> dict[str, str]:
    return {
        "status": "ok",
        "service": "shipping_stub",
        "mode": current_mode(),
    }


@app.post("/labels")
async def create_label(payload: dict[str, Any] | None = Body(default=None)) -> JSONResponse:
    payload = payload or {}
    mode = current_mode()
    correlation_id = payload.get("correlationId")
    order_id = payload.get("orderId", "unknown")

    if mode == "slow":
        await asyncio.sleep(delay_seconds())

    if mode == "outage":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={
                "status": "unavailable",
                "reason": "shipping_simulated_outage",
                "correlationId": correlation_id,
            },
        )

    return JSONResponse(
        status_code=status.HTTP_201_CREATED,
        content={
            "status": "label_created",
            "trackingNumber": f"TRK-{order_id}",
            "carrier": "StubCarrier",
            "correlationId": correlation_id,
        },
    )
