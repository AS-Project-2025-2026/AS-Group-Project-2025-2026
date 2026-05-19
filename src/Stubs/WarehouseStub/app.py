import asyncio
import os
from typing import Any

from fastapi import Body, FastAPI, status
from fastapi.responses import JSONResponse


app = FastAPI(title="VerdeMart Warehouse Stub")


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
        "service": "warehouse_stub",
        "mode": current_mode(),
    }


@app.post("/fulfillment")
async def request_fulfillment(payload: dict[str, Any] | None = Body(default=None)) -> JSONResponse:
    payload = payload or {}
    mode = current_mode()
    correlation_id = payload.get("correlationId")
    order_id = payload.get("orderId", "unknown")

    if mode in {"slow", "delayed"}:
        await asyncio.sleep(delay_seconds())

    if mode == "failed":
        return JSONResponse(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            content={
                "status": "failed",
                "reason": "warehouse_simulated_failure",
                "correlationId": correlation_id,
            },
        )

    return JSONResponse(
        status_code=status.HTTP_202_ACCEPTED,
        content={
            "status": "picked",
            "warehouseReference": f"WH-{order_id}",
            "correlationId": correlation_id,
        },
    )
