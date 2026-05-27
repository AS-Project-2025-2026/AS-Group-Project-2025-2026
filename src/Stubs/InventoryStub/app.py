import asyncio
import os
import random
from typing import Any

from fastapi import FastAPI, status
from fastapi.responses import JSONResponse

app = FastAPI(title="VerdeMart Inventory Stub")

# In-memory stock: productId -> quantity. Seeded from env or defaults.
_stock: dict[int, int] = {}


def _init_stock() -> None:
    raw = os.getenv("STUB_PRODUCT_IDS", "1,2,3,4,5")
    ids = [int(x.strip()) for x in raw.split(",") if x.strip().isdigit()]
    for pid in ids:
        _stock[pid] = random.randint(10, 200)


_init_stock()


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
    return {"status": "ok", "service": "inventory_stub", "mode": current_mode()}


@app.get("/stock")
async def get_stock() -> JSONResponse:
    mode = current_mode()

    if mode == "unavailable":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content={"status": "unavailable", "reason": "inventory_simulated_outage"},
        )

    if mode == "slow":
        await asyncio.sleep(delay_seconds())

    if mode == "stale":
        # Return deliberately wrong (stale) quantities to simulate lag.
        stale = {pid: max(0, qty - random.randint(5, 30)) for pid, qty in _stock.items()}
        return JSONResponse(
            content={
                "status": "stale",
                "warning": "data_may_be_outdated",
                "products": [{"productId": pid, "quantity": qty} for pid, qty in stale.items()],
            }
        )

    return JSONResponse(
        content={
            "status": "ok",
            "products": [{"productId": pid, "quantity": qty} for pid, qty in _stock.items()],
        }
    )


@app.post("/stock/adjust")
async def adjust_stock(body: dict[str, Any]) -> JSONResponse:
    """Simulates a warehouse/store reducing stock after a sale or pick."""
    product_id = body.get("productId")
    delta = body.get("delta", 0)

    if product_id is None or product_id not in _stock:
        return JSONResponse(
            status_code=status.HTTP_404_NOT_FOUND,
            content={"status": "not_found", "productId": product_id},
        )

    _stock[product_id] = max(0, _stock[product_id] + delta)
    return JSONResponse(
        content={"status": "adjusted", "productId": product_id, "newQuantity": _stock[product_id]}
    )
