import asyncio
import os
import random
from typing import Any

from fastapi import FastAPI, status
from fastapi.responses import JSONResponse

app = FastAPI(title="VerdeMart Inventory Stub")

# WMS stock: productId -> quantity. Seeded from env or defaults.
_stock: dict[int, int] = {}

# POS-reported stock: productId -> quantity. Set via POST /stock/pos-report.
# If a product has no POS entry it is not considered in conflict.
_pos_stock: dict[int, int] = {}


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

    products = []
    for pid, wms_qty in _stock.items():
        entry: dict[str, Any] = {"productId": pid, "wmsQuantity": wms_qty}
        if pid in _pos_stock:
            entry["posQuantity"] = _pos_stock[pid]
            entry["conflict"] = abs(wms_qty - _pos_stock[pid]) > int(os.getenv("CONFLICT_TOLERANCE", "2"))
        products.append(entry)

    return JSONResponse(content={"status": "ok", "products": products})


@app.post("/stock/pos-report")
async def pos_report(body: dict[str, Any]) -> JSONResponse:
    """Simulate a POS system reporting a stock quantity different from the WMS.

    Body: { "productId": int, "quantity": int }

    This is the trigger for QAS 6 (inventory conflict between POS and WMS).
    Calling this with a quantity that diverges from the WMS value by more than
    CONFLICT_TOLERANCE units (default 2) will cause GET /stock to flag a conflict
    on that product, which the Integration Worker should detect and surface as a
    reconciliation task in the Operations View.
    """
    product_id = body.get("productId")
    quantity = body.get("quantity")

    if product_id is None:
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content={"status": "error", "reason": "productId is required"},
        )
    if quantity is None or not isinstance(quantity, int) or quantity < 0:
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content={"status": "error", "reason": "quantity must be a non-negative integer"},
        )

    _pos_stock[product_id] = quantity

    wms_qty = _stock.get(product_id)
    conflict = (
        wms_qty is not None
        and abs(wms_qty - quantity) > int(os.getenv("CONFLICT_TOLERANCE", "2"))
    )

    return JSONResponse(
        content={
            "status": "reported",
            "productId": product_id,
            "posQuantity": quantity,
            "wmsQuantity": wms_qty,
            "conflict": conflict,
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
