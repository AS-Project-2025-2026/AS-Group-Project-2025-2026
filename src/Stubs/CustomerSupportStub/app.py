import asyncio
import os
from typing import Any

from fastapi import Body, FastAPI, status
from fastapi.responses import JSONResponse

app = FastAPI(title="VerdeMart Customer Support Stub")


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
        "service": "customersupport_stub",
        "mode": current_mode(),
    }


@app.post("/tickets")
async def create_ticket(payload: dict[str, Any] | None = Body(default=None)) -> JSONResponse:
    payload = payload or {}
    mode = current_mode()
    correlation_id = payload.get("correlationId")
    order_id = payload.get("orderId", "unknown")
    reason = payload.get("reason", "general")

    if mode == "slow":
        await asyncio.sleep(delay_seconds())

    if mode == "offline":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={
                "status": "unavailable",
                "reason": "customersupport_simulated_offline",
                "correlationId": correlation_id,
            },
        )

    return JSONResponse(
        status_code=status.HTTP_201_CREATED,
        content={
            "status": "ticket_created",
            "ticketId": f"TKT-{order_id}-{reason[:8].upper()}",
            "correlationId": correlation_id,
        },
    )
